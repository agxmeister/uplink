# Uplink — architecture and contribution guide

Uplink is a Unity **Editor-only** package that exposes the Editor as a small REST API described by
`GET /openapi.json`, so AI assistants reach it through an off-the-shelf OpenAPI-to-MCP adapter. There is no MCP
code in this repository, and nothing ships in game builds.

This file is for people (and assistants) working *on* Uplink. For using it, see [`README.md`](README.md).

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
  Api/       IEndpoint, Router, FaultBarrier, MainThreadEndpoint, OpenApiEndpoint, Schema
  Http/      HttpListenerServer, IRequestHandler, Request, Response, Route
  Status/    StatusEndpoint + IEditorStatusProbe/UnityEditorStatusProbe + EditorStatus
  Threading/ IMainThreadDispatcher, MainThreadDispatcher (no Unity dependency — pumpable in tests)
  Configuration/, Diagnostics/
Tests/Editor/             EditMode tests + fakes
```

Group a new endpoint's files in their own folder next to `Status/`, the way `Status/` holds its endpoint, its
probe abstraction and its payload together.

## Adding an endpoint

1. **Write the payload** as a POCO with `[JsonProperty("camelCase")]` names — that is the wire contract.
2. **Abstract the Editor access** behind a small interface with a Unity-facing implementation, as
   `IEditorStatusProbe` / `UnityEditorStatusProbe` do. Endpoints never call `UnityEditor` directly.
3. **Implement `IEndpoint`.** `Method` and `Path` are what the router dispatches on. `Describe()` returns an
   OpenAPI Operation Object — build it with `Schema.Operation(...)`, and its `operationId` becomes the tool name
   the adapter exposes, so keep it a short verb (`status`, `read_console`). `Handle()` returns a `Response`.
4. **Register it** in `Uplink`'s static constructor, before `OpenApiEndpoint`. Wrap it in `OnMainThread(...)` if
   it touches `UnityEditor` APIs.
5. **Test it** without an Editor: feed it a stub probe and assert on the response, and assert that everything it
   returns is also described (see `StatusEndpointTests`).
6. **Document it** in the README's Features table.

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

## Tests

EditMode tests live in `Tests/Editor`. Unity only discovers a package's tests when the consuming project opts
in — add this to its `Packages/manifest.json`:

```json
"testables": [ "com.agxmeister.uplink" ]
```

Then run them from `Window → General → Test Runner`, under *EditMode*.

`MainThreadDispatcher` has no Unity dependency, so tests pump it by hand from a worker thread rather than
waiting on `EditorApplication.update`; `InlineDispatcher` in `Fakes.cs` stands in when the threading itself is
not under test.

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
