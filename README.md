# Uplink

**MCP remote control for the Unity Editor.**

Uplink is a Unity Editor plugin that exposes the Editor as a small RESTful API, described by OpenAPI, so AI assistants can drive it through any OpenAPI-to-[MCP](https://modelcontextprotocol.io) adapter. It closes the feedback loop for AI-assisted Unity development: your assistant can compile, read console output, run tests, and *see* the scene through screenshots — instead of editing scripts blindly.

Works with Claude Code, Claude Desktop, Cursor, and any other MCP client.

> Uplink runs entirely inside the Editor as an editor-only assembly. Nothing is included in your game builds.

## Why

AI coding assistants are great at writing Unity C#, but by default they are blind to the Editor: they can't see compile errors, console logs, test results, or what the scene actually looks like. Uplink gives them exactly that feedback channel — small, focused, and nothing more.

## Features

Uplink deliberately ships a compact, feedback-loop-first toolset. Version 0.1.0 ships the first one:

| Endpoint | Tool | What it does |
|---|---|---|
| `GET /status` | `status` | Report the Editor: Unity version, platform, project, build target, active scene, play mode |
| `GET /console` | `read_console` | *(planned)* Read Editor console messages (errors, warnings, logs), with filtering and paging |
| `POST /refresh` | `refresh` | *(planned)* Trigger asset database refresh / script recompilation and report compile errors |
| `POST /tests` | `run_tests` | *(planned)* Run EditMode / PlayMode tests via the Unity Test Framework and return structured results |
| `GET /screenshot` | `screenshot` | *(planned)* Capture the Game view, Scene view, or a specific camera as a PNG |

That's it, by design. The assistant writes code with its own file tools; Uplink tells it whether the code compiles, passes tests, and looks right.

## Requirements

- Unity 2021.3 LTS or newer
- An MCP-capable AI client (Claude Code, Claude Desktop, Cursor, …)
- An OpenAPI-to-MCP adapter, e.g. [`@ivotoby/openapi-mcp-server`](https://github.com/ivo-toby/mcp-openapi-server) (Node)

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

**2. Point an adapter at the spec** — for Claude Code:

```
claude mcp add uplink -- npx -y @ivotoby/openapi-mcp-server \
  --api-base-url http://localhost:8787 \
  --openapi-spec http://localhost:8787/openapi.json
```

Any other adapter works the same way: it only needs the base URL and `/openapi.json`. Operation ids in the spec become
the tool names.

## Quickstart

Ask your assistant:

> "Call uplink status — which Unity version and scene am I on?"

## How it works

```
AI client (Claude Code, Cursor, …)
        │  MCP protocol
        ▼
OpenAPI-to-MCP adapter
        │  HTTP on http://localhost:8787, tools generated from /openapi.json
        ▼
Uplink Editor plugin (this package)
        │  UnityEditor API, main thread
        ▼
Console · Compilation · Test Runner · Cameras
```

The plugin is a plain REST API — one endpoint per tool, self-described by `GET /openapi.json` — and contains no MCP
code at all. It marshals each request onto
the Editor main thread, executes it against the `UnityEditor` APIs, and returns JSON. MCP is left entirely to an
off-the-shelf adapter, which keeps this package small and every endpoint reachable with `curl` while debugging.

## Recommended CLAUDE.md snippet

If you use Claude Code, add something like this to *your Unity project's* `CLAUDE.md` (this repository's
[`CLAUDE.md`](CLAUDE.md) is a different thing — it documents Uplink's own internals for contributors):

```markdown
## Unity workflow
- After editing any C# script, call uplink `refresh` and fix reported compile errors before proceeding.
- Verify behavior with uplink `run_tests`; verify visuals with uplink `screenshot`.
- Never edit .unity, .prefab, or .asset YAML files directly.
```

## Roadmap

- [ ] Scene hierarchy inspection (read-only)
- [ ] Play mode enter/exit control
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
