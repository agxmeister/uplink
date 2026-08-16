# Uplink — architecture and contribution guide

Uplink is a Unity **Editor-only** package that exposes the Editor as a small REST API described by
`GET /openapi.json`, so AI assistants reach it through an off-the-shelf OpenAPI-to-MCP adapter. There is no MCP
code in this repository, and nothing ships in game builds.

This file is for people (and assistants) working *on* Uplink. For using it, see [`README.md`](README.md). It
says what the rules are; [`Documentation~/adr`](Documentation~/adr) says why they were chosen and what they
were chosen over. Read the relevant record before arguing with a rule here.

## Principles

**One endpoint per tool, and the endpoint owns its whole contract.** An `IEndpoint` knows its own method, path,
OpenAPI description and behaviour. Adding a tool must never require editing the router or the spec.

**The spec is derived, never maintained.** `Router` and `OpenApiEndpoint` read the *same* endpoint collection, so
a live route and its published description cannot drift apart. Never hand-write a path into the document.

**Depend on abstractions, not on `UnityEditor`.** Endpoints take an injected interface (`IEditorStatusProbe`,
`IUplinkLog`, `IMainThreadDispatcher`) and stay testable outside a running Editor. Exactly one class per concern
is allowed to touch the Unity statics, and it does nothing else — see `UnityEditorStatusProbe`, `UnityLog`,
`EditorPrefsSettings`.

**`Uplink` is the only place that names concrete types.** It is the composition root. Everything else receives
its collaborators through its constructor.

**Cross-cutting behaviour is a decorator, not a base class or a flag.** Main-thread marshalling and error
shaping wrap the pipeline rather than being sprinkled through it.

**Anything that outlives a domain reload is a service plus a pure log.** An endpoint only answers; something
has to be listening to the Editor before the request arrives. That something is an `IUplinkService`, and the
state it gathers lives in a Unity-free class beside it — `ConsoleBuffer`, `CompileLog`, `TestLog` — so the
interesting logic is testable and only the thin service touches `UnityEditor`.

## Structure

A request travels a single path, with each layer knowing only the next:

```
HttpListenerServer   sockets and threads only — no routes, no payloads, no error semantics
  └─ FaultBarrier    turns anything thrown below into a response: TimeoutException → 504, else 500
      └─ Router      matches method + path → 404 (no such path) or 405 + Allow (wrong verb)
          └─ MainThreadEndpoint   marshals onto the Editor main thread, with a timeout
              └─ StatusEndpoint   the actual work
```

```
Editor/
  Uplink.cs               composition root; [InitializeOnLoad] entry point and lifecycle
  UplinkWindow.cs         Window → Uplink
  Api/          IEndpoint, Router, FaultBarrier, MainThreadEndpoint, OpenApiEndpoint, Schema
  Http/         HttpListenerServer, IRequestHandler, Request, Response, Route, Arguments
  Services/     IUplinkService — the components that run outside a request
  Persistence/  ISessionStore/SessionStateStore, Stored — state that survives a domain reload
  Threading/    IMainThreadDispatcher, MainThreadDispatcher (no Unity dependency — pumpable in tests)
  Status/       StatusEndpoint + IEditorStatusProbe/UnityEditorStatusProbe + EditorStatus
  Console/      ConsoleEndpoint + ConsoleCollector (service) + ConsoleBuffer + UnityConsoleHistory
  Compilation/  CompileEndpoint + UnityCompiler (service) + CompileLog
  Testing/      TestsEndpoint + UnityTestRunner (service) + TestLog
  PlayMode/     PlayModeEndpoint + PlayModeControl + UnityPlayMode (service)
  Capture/      ScreenshotEndpoint + IViewCapture/UnityViewCapture
  Hierarchy/    SceneEndpoint, ObjectEndpoint + ISceneProbe/UnitySceneProbe + SerializedValues
  Configuration/, Diagnostics/
Tests/Editor/             EditMode tests + fakes
```

Group a new endpoint's files in their own folder next to `Status/`, the way `Status/` holds its endpoint, its
probe abstraction and its payload together. Name the folder for the concern, not for the Unity type it wraps —
`Hierarchy/` rather than `Scene/`, `Capture/` whose result type is `CaptureResult` — because a namespace and a
type of the same name do not coexist.

## The self-polling cycle

`compile`, `run_tests` and `set_play_mode` all set off work that outlives the request asking for it:
compiling and entering play mode reload the script domain, which wipes every static and closes the listener.
The request that starts such work therefore cannot be the request that reports it.

Rather than job ids and a separate polling endpoint — which would need path templates in `Router`, whose
`Route.Matches` is exact-equality only, and would give every tool a second tool beside it — one endpoint runs a
three-state cycle:

```
idle      a call starts the work, becomes running, answers 202
running   a call reports progress and changes nothing, answers 202 (200 once it has a result to give)
done      a call hands the result over and returns to idle, answers 200
```

The result is delivered exactly once, so the call after it unambiguously means "do it again". `202` lets a
client tell "not finished" from "finished" without reading the body. The state lives in `ISessionStore`, whose
lifetime — surviving a reload, cleared on Editor restart — is exactly right for it.

For `compile`, "the work" does not end at the compiler's last word: a successful build reloads the domain, and
the reload is what re-runs `[InitializeOnLoadMethod]` setup code. So the run stays `202` until a couple of
quiet ticks after the reload, and the `done` result carries a `console` page of everything the run logged —
see [ADR-0010](Documentation~/adr/0010-compile-reports-the-reload-it-causes.md). `{"force": true}` reloads
even when nothing changed, which is how such setup code is re-run without touching a file. And because play
mode makes those reloads silently run nothing, `compile` and `refresh` report `isPlaying` in every response.

`set_play_mode` is the exception that proves the rule: it needs no stored state, because the Editor itself
records whether it is playing. It compares what was asked for with what is, and asks again if they differ.

A new endpoint of this shape follows `CompileLog`: the cycle in a Unity-free class whose `Advance` returns both
the answer and whether the caller should now go and do the thing, and a service that does it.

## Adding an endpoint

1. **Write the payload** as a POCO with `[JsonProperty("camelCase")]` names — that is the wire contract.
2. **Abstract the Editor access** behind a small interface with a Unity-facing implementation, as
   `IEditorStatusProbe` / `UnityEditorStatusProbe` do. Endpoints never call `UnityEditor` directly.
3. **Implement `IEndpoint`.** `Method` and `Path` are what the router dispatches on. `Describe()` returns an
   OpenAPI Operation Object — build it with `Schema.Operation(...)`. `Handle()` returns a `Response`.
   Keep `operationId` a short verb (`status`, `read_console`): adapters that generate one tool per operation use
   it as the tool name. But whispr, the adapter Uplink is developed against, never shows it — it lists endpoints
   by their `summary` — so the prose carries the interface either way. See
   [ADR-0008](Documentation~/adr/0008-endpoint-prose-is-the-interface.md).
   Read inputs through `Arguments`, never from `Request.Query` or `Request.Body` directly — it is the one place
   a malformed input turns into a `400`, the way `Route.Normalize` is the one place a path is normalized. The
   `description` is the only documentation the model driving the tool will ever read: say what the tool answers
   and how to use its result, not how it is implemented.
4. **Register it** in `Uplink`'s static constructor, before `OpenApiEndpoint`. Wrap it in `OnMainThread(...)` if
   it touches `UnityEditor` APIs.
5. **Test it** without an Editor: feed it a stub probe and assert on the response, and assert that everything it
   returns is also described (see `StatusEndpointTests`).
6. **Document it** in the README's Features table — and if it settled something a later reader would want to
   argue with, add a record to [`Documentation~/adr`](Documentation~/adr).

## Conventions

- **The main-thread rule.** `UnityEditor` and `UnityEngine` APIs are legal only on the Editor main thread. HTTP
  requests arrive on thread-pool threads, so any endpoint reading Editor state must be wrapped in
  `OnMainThread(...)`. Code reached that way should be quick — it blocks the Editor while it runs.
- **Non-JSON responses** are already supported: `Response` carries bytes, so use `Response.Bytes(...)` for a PNG
  screenshot rather than base64 in a JSON field.
- **Every failure is `{"error": "..."}`** via `Response.Error(...)`. Let exceptions propagate to `FaultBarrier`
  instead of catching and shaping them per endpoint.
- **Timeouts are not faults.** A busy Editor (compiling, importing, modal dialog) must read as `504` so the
  client retries; `500` means a real bug.
- **Paths** are normalized in one place, `Route.Normalize` — leading slash, no trailing slash. Don't compare
  paths by hand.
- **Code style** matches the existing files and deliberately targets older Unity: `get { return ...; }` over
  expression-bodied members, `string.Format` over interpolation, explicit `ArgumentNullException` guards in
  constructors. Follow it rather than modernizing.
- **The package must stay Editor-only.** Keep `includePlatforms: ["Editor"]` in the asmdef; that is also what
  makes `System.Net.HttpListener` available, since the Editor runs on the full .NET profile.
- **Work that reloads the domain is deferred by a tick.** `AssetDatabase.Refresh` and
  `EditorApplication.isPlaying` do their thing inside the call, so doing them in `Handle` would close the
  listener before the answer could be written. `UnityCompiler` and `UnityPlayMode` both set a flag and act on
  the next `EditorApplication.update` instead, which is what lets the client be told the work has begun.

## Tests

EditMode tests live in `Tests/Editor`. Unity only discovers a package's tests when the consuming project opts
in — add this to its `Packages/manifest.json`:

```json
"testables": [ "com.agxmeister.uplink" ]
```

Then run them from `Window → General → Test Runner`, under *EditMode*.

`MainThreadDispatcher` has no Unity dependency, so tests pump it by hand from a worker thread rather than
waiting on `EditorApplication.update`; `InlineDispatcher` in `Fakes.cs` stands in when the threading itself is
not under test. `InMemoryStore` stands in for `SessionState`, which lets a domain reload be written as a test:
capture one log's state through the store, restore it into a fresh instance, and carry on.

Two things are worth asserting about every endpoint, and both have precedents to copy: that each field it
returns is also described (`StatusEndpointTests.DescribesEveryFieldItActuallyReturns`), and that each parameter
it reads is described too (`ConsoleEndpointTests.DescribesEveryParameterItAccepts`). A tool the adapter cannot
see is a tool that does not exist.

## Gotchas

- **Domain reloads restart everything.** Statics are wiped whenever scripts recompile. `Uplink` stops the
  listener on `AssemblyReloadEvents.beforeAssemblyReload`; if a port is ever left bound, that hook is the first
  place to look.
- **The port is per-machine, in `EditorPrefs`** — not per-project, since two Editors cannot share one port.
- **Work abandoned on timeout must not run later.** `MainThreadDispatcher` marks it, deliberately: without that,
  a call whose caller already gave up would execute against the Editor at an arbitrary later moment.
- **No authentication.** The `http://localhost:{port}/` prefix binds loopback only, but any local process —
  including a browser page — can reach the API. Fine for a dev tool; revisit if a mutating endpoint ever does
  something expensive or destructive.
- **A `POST` with no `Content-Length` is answered `411` before Uplink runs.** Mono's `HttpListener` rejects it
  while parsing the request, so no code in this repository can accept it; `curl -X POST` alone sends none.
  This is documented for clients (send `-d '{}'`) rather than fixed, because fixing it means leaving
  `HttpListener`.
