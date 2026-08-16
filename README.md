# Uplink

**MCP remote control for the Unity Editor.**

Uplink is a Unity Editor plugin that exposes the Editor as a small RESTful API, described by OpenAPI, so AI assistants can drive it through any OpenAPI-to-[MCP](https://modelcontextprotocol.io) adapter. It closes the feedback loop for AI-assisted Unity development: your assistant can compile, read console output, run tests, and *see* the scene through screenshots — instead of editing scripts blindly.

Works with Claude Code, Claude Desktop, Cursor, and any other MCP client.

> Uplink runs entirely inside the Editor as an editor-only assembly. Nothing is included in your game builds.

## Why

AI coding assistants are great at writing Unity C#, but by default they are blind to the Editor: they can't see compile errors, console logs, test results, or what the scene actually looks like. Uplink gives them exactly that feedback channel — small, focused, and nothing more.

## Features

Uplink deliberately ships a compact, feedback-loop-first toolset:

| Endpoint | Tool | What it does |
|---|---|---|
| `GET /status` | `status` | Report the Editor: Unity version, platform, project, build target, active scene, unsaved changes, play mode |
| `POST /compile` | `compile` | Build the scripts, follow the reload, and report compiler errors plus what the reload logged; `force: true` reloads even when nothing changed |
| `GET /console` | `read_console` | Read console messages, filtered by severity and text, from a cursor so each is seen once |
| `POST /tests` | `run_tests` | Run the EditMode or PlayMode suite and report which tests failed, and why |
| `GET /screenshot` | `screenshot` | Capture a camera, the Game view or the Scene view as a PNG — inline, cropped, or written to a file |
| `GET /scene` | `read_scene` | List the objects in the open scenes, with their paths and components |
| `GET /object` | `read_object` | Read one GameObject's components and their serialized values, narrowed to named fields or components |
| `POST /play` | `set_play_mode` | Enter, leave, pause or step play mode |
| `POST /refresh` | `refresh` | Make the Editor re-read files changed on disk, re-opening the open scenes |

That's it, by design. The assistant writes code with its own file tools; Uplink tells it whether the code compiles, passes tests, and looks right.

Four of these — `compile`, `run_tests`, `set_play_mode` and `refresh` — do something that outlives the request that asked for it, because compiling, importing and entering play mode reload the Editor's script domain and take the HTTP listener with them. They are therefore **called repeatedly rather than waited on**: the first call starts the work and answers `202`, and a later call returns the result and resets, so the call after that starts the next run. The tool descriptions in `/openapi.json` spell this out, so an assistant reading them gets it right without being told.

`compile` in particular reports `done` only once the domain reload a successful build causes has finished, and its result carries what that reload logged — which is how `[InitializeOnLoadMethod]` setup scripts are driven and observed with one tool. Since such scripts silently do nothing in play mode, every `compile` and `refresh` response also says `isPlaying`.

One sharp edge when driving these by hand: the .NET listener Uplink sits on rejects a `POST` that has no `Content-Length` at all with `411`, before Uplink sees it. `curl -X POST` alone sends none — add a body (`-d '{}'`) or `-H 'Content-Length: 0'`.

## Requirements

- Unity 2021.3 LTS or newer
- `com.unity.test-framework` and `com.unity.nuget.newtonsoft-json`, both installed automatically as package dependencies
- An MCP-capable AI client (Claude Code, Claude Desktop, Cursor, …)
- An OpenAPI-to-MCP adapter — [whispr](https://github.com/agxmeister/whispr) is the one Uplink is developed against (Node 20+)

## Installation

**1. Install the Unity package** — via Package Manager (git URL):

1. Open `Window → Package Manager`
2. Click `+` → *Add package from git URL…*
3. Enter:

```
https://github.com/agxmeister/uplink.git
```

Open your project; Uplink starts with the Editor. Check `Window → Uplink` for status and the port, or:

```
curl http://localhost:8787/status
```

**2. Point an adapter at the spec.** With [whispr](https://github.com/agxmeister/whispr), Uplink is an *edge* —
drop this in its `edges/uplink.json`:

```json
{
    "name": "Uplink",
    "description": "Uplink exposes a running Unity Editor, so that a change to a project can be compiled, tested and looked at.",
    "tasks": [
        "compile the project's scripts and read the errors",
        "read the Editor console",
        "run EditMode and PlayMode tests",
        "capture the game or scene as an image",
        "inspect the objects in the open scenes",
        "enter and leave play mode"
    ],
    "api": {
        "specification": {
            "url": "{{HOST}}/openapi.json"
        },
        "request": {
            "url": "{{HOST}}"
        }
    },
    "environment": [{
        "name": "HOST",
        "description": "The base URL of the Unity Editor running Uplink, as shown in Window > Uplink (e.g. http://localhost:8787)."
    }]
}
```

Then build whispr's configuration and register it with your client, as its README describes. Do **not** give
this edge whispr's read-only profile: that filters the spec down to `GET`, which hides `compile`, `run_tests`
and `set_play_mode` — most of the feedback loop.

Any other adapter works too; it only needs the base URL and `/openapi.json`. Note that adapters differ in how
much of the spec they show the model — whispr has it list endpoints and read their descriptions, while others
generate one tool per operation and use `operationId` as its name. Uplink writes for both: see
[ADR-0008](Documentation~/adr/0008-endpoint-prose-is-the-interface.md).

## Quickstart

Ask your assistant:

> "Call uplink status — which Unity version and scene am I on?"

## How it works

```
AI client (Claude Code, Cursor, …)
        │  MCP protocol
        ▼
OpenAPI-to-MCP adapter (whispr)
        │  HTTP on http://localhost:8787, tools driven by /openapi.json
        ▼
Uplink Editor plugin (this package)
        │  UnityEditor API, main thread
        ▼
Console · Compilation · Test Runner · Cameras · Scene graph
```

The plugin is a plain REST API — one endpoint per tool, self-described by `GET /openapi.json` — and contains no MCP
code at all. It marshals each request onto
the Editor main thread, executes it against the `UnityEditor` APIs, and returns JSON. MCP is left entirely to an
off-the-shelf adapter, which keeps this package small and every endpoint reachable with `curl` while debugging.

The reasoning behind that split, and the other decisions that shape the package, is recorded in
[`Documentation~/adr`](Documentation~/adr).

## Recommended CLAUDE.md snippet

If you use Claude Code, add something like this to *your Unity project's* `CLAUDE.md` (this repository's
[`CLAUDE.md`](CLAUDE.md) is a different thing — it documents Uplink's own internals for contributors):

```markdown
## Unity workflow
- After editing any C# script, call uplink `compile` and fix every reported error before going on.
  It answers 202 while it builds — call it again until `state` is `done`. The `done` result carries
  the console output of the reload, so what an [InitializeOnLoadMethod] script logged arrives with it.
- To re-run [InitializeOnLoadMethod] setup code when no script changed, call `compile` with
  `force: true` instead of touching a file. If `isPlaying` is true, leave play mode first — reloads
  run no setup code while the game plays.
- Then call uplink `run_tests`; failures come back with the assertion message and stack trace.
- Verify visuals with uplink `screenshot` — pass `path` to write the PNG to a file you can read, and
  `crop=x,y,w,h` to inspect a detail — and check that a change landed on the object you meant with
  uplink `read_scene` / `read_object` (narrow with `fields=` / `components=`).
- Check `sceneDirty` in uplink `status` when work must be saved, not only look right.
- Never edit .unity, .prefab, or .asset YAML files directly.
```

## Roadmap

- [ ] Editing the scene, not only reading it
- [ ] Multi-instance routing (several open Editors)
- [ ] OpenUPM package

## Contributing

Issues and PRs welcome. Keep tools small and focused — Uplink's goal is a tight feedback loop, not full Editor
automation.

Adding an endpoint is one new class plus one line of registration. See [`CLAUDE.md`](CLAUDE.md) for the
architecture, the conventions it expects, and how to run the tests.

## License

MIT

---

*Uplink is not affiliated with Unity Technologies. "Unity" is a trademark of Unity Technologies.*
