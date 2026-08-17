# ADR-0015 — Optional capabilities register themselves

- **Status:** Accepted
- **Date:** 2026-08-17
- **Amends:** [CLAUDE.md](../../CLAUDE.md)'s rule that `Uplink` is the only place that names concrete types,
  and that an endpoint is registered in its static constructor. Both keep their force; this record carves the
  one exception and says what it costs.
- **Implements:** REQ-0001 B, behaviour §3

## Context

`POST /input` needs the Input System package. Uplink must not.

REQ-0001 is blunt about why the tool cannot simply be registered unconditionally: *a tool the adapter can see
but that cannot work is worse than no tool.* An endpoint that appears in `/openapi.json` and answers `500`
because a type failed to load is a trap laid for a model that read the spec and believed it.

So the endpoint must be **absent** — from the router, and therefore from the published spec — when the
package is absent. Three ways to get there:

| Approach | Verdict |
|---|---|
| One assembly, `versionDefines` + `"references": ["Unity.InputSystem"]`, code behind `#if` | **Rejected.** The reference is unresolvable when the package is absent. On 2021.3 that is a hard asmdef error, so the package would stop compiling for every project *without* the Input System — the exact projects this was meant to protect. |
| Reflection over `InputSystem.QueueStateEvent` from the main assembly | **Rejected.** Naming types by string, and discovering typos at runtime, is worse than the problem it solves. `InputSystem`'s state-event API is not a shape worth addressing blind. |
| **A second assembly, not compiled at all when the package is absent** | **Chosen.** |

## Decision

**`Editor/Controls/InputSystem/` is its own assembly, and it disappears when the package does.**

```json
"references": [ "Agxmeister.Uplink.Editor", "Unity.InputSystem" ],
"versionDefines": [
  { "name": "com.unity.inputsystem", "expression": "1.0.0", "define": "UPLINK_INPUT_SYSTEM" }
],
"defineConstraints": [ "UPLINK_INPUT_SYSTEM" ]
```

Version defines are evaluated **before** constraints. With no Input System present, `UPLINK_INPUT_SYSTEM` is
never defined, the constraint fails, and the assembly is not compiled — so its unresolvable reference to
`Unity.InputSystem` is never resolved. That ordering is the whole trick, and it is why this works where the
single-assembly version does not.

**Which forces the composition root to accept registrations it did not make.** `Uplink` cannot name a type in
an assembly that may not exist, so the dependency inverts:

```csharp
public static void Register(IUplinkService service, params IEndpoint[] endpoints)
```

called from an `[InitializeOnLoadMethod]` in the optional assembly. `params`, because this registers one
driver with **two** endpoints — `POST /input` and its read-only twin (ADR-0012) — and calling `Register`
twice would attach the service twice.

**Ordering is deterministic, and not by luck.** Unity runs `[InitializeOnLoad]` static constructors before
`[InitializeOnLoadMethod]` methods; and independently of that, touching `Uplink.Register` triggers `Uplink`'s
static constructor first, because that is what the CLR does to a static class on first access. Either rule
alone is enough.

**What makes this safe is an invariant that was already there.** `Router` and `OpenApiEndpoint` are both
handed the endpoint *collection*, not a copy of it, and both read it per request. So an endpoint that arrives
late appears in the live routes and in the published description at the same instant, and the ADR-0001
promise — the spec is derived, never maintained — survives a registration the composition root never saw.

**One wrinkle, and it is a real race.** `Start()` runs at the end of `Uplink`'s static constructor, so the
listener can already be serving requests on a thread-pool thread when the optional assembly's
`[InitializeOnLoadMethod]` fires on the main thread. Appending to a `List<T>` that another thread is
enumerating throws `InvalidOperationException` — which `FaultBarrier` would turn into a `500` on an unrelated
request, intermittently, in a window a few milliseconds wide. That is the worst kind of bug to leave for
later.

It is guarded once, in one place: **`EndpointRegistry`**, an `IEnumerable<IEndpoint>` whose `Add` and whose
`GetEnumerator` take the same lock, and whose enumerator walks a snapshot. `Router` and `OpenApiEndpoint` are
unchanged — they still take an `IEnumerable<IEndpoint>` and still know nothing about registration — and no
call site has to remember to lock, because there is no unlocked path to forget.

## Consequences

- A project without the Input System gets a package that compiles, and an `/openapi.json` with no `/input` in
  it. A model driving that Editor never learns of a tool it cannot use.
- `Uplink` is no longer the *only* place that names concrete types — but it is still the only place that
  names concrete types it *can*. The rule that changed is narrower than it looks: a capability that may not
  exist registers itself, and everything else is registered where it always was.
- The registration point is public API. `Uplink.Register` can be called by anything in the project, not only
  by this assembly, which makes third-party endpoints possible as a side effect. That was not the goal and is
  not documented as a feature; if it ever becomes one it needs its own thought about trust and naming
  collisions.
- Two endpoints now exist that the main test assembly can construct but the main *package* may not serve.
  `ApiSurfaceTests` therefore asserts both halves: the surface with the input endpoints, and — the case that
  matters — that the surface is valid *without* them.
- The optional assembly holds only Unity-facing code. The cycle, the step schedule and the payload live in
  the main assembly against `IInputDriver`, so nothing untestable was added and the fake-clock test needs no
  Input System either.
- `defineConstraints` failing produces no warning and no error — the assembly is simply skipped. That is the
  behaviour wanted, and it is also the failure mode to remember when `/input` is unexpectedly missing: check
  the package, not the code.
