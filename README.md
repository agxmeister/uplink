# Uplink

**MCP remote control for the Unity Editor.**

Uplink is a Unity Editor plugin that exposes the Editor to AI assistants via the [Model Context Protocol (MCP)](https://modelcontextprotocol.io). It closes the feedback loop for AI-assisted Unity development: your assistant can compile, read console output, run tests, and *see* the scene through screenshots — instead of editing scripts blindly.

Works with Claude Code, Claude Desktop, Cursor, and any other MCP client.

> Uplink runs entirely inside the Editor as an editor-only assembly. Nothing is included in your game builds.

## Why

AI coding assistants are great at writing Unity C#, but by default they are blind to the Editor: they can't see compile errors, console logs, test results, or what the scene actually looks like. Uplink gives them exactly that feedback channel — small, focused, and nothing more.

## Features

Uplink deliberately ships a compact, feedback-loop-first toolset:

| Tool | What it does |
|---|---|
| `read_console` | Read Editor console messages (errors, warnings, logs), with filtering and paging |
| `refresh` | Trigger asset database refresh / script recompilation and report compile errors |
| `run_tests` | Run EditMode / PlayMode tests via the Unity Test Framework and return structured results |
| `screenshot` | Capture the Game view, Scene view, or a specific camera as a PNG — returned inline so the assistant can see it |

That's it, by design. The assistant writes code with its own file tools; Uplink tells it whether the code compiles, passes tests, and looks right.

## Requirements

- Unity 2021.3 LTS or newer
- An MCP-capable AI client (Claude Code, Claude Desktop, Cursor, …)

## Installation

**Via Package Manager (git URL):**

1. Open `Window → Package Manager`
2. Click `+` → *Add package from git URL…*
3. Enter:

```
https://github.com/agxmeister/uplink.git
```

## Quickstart

1. Install the package (above) and open your project. Uplink starts with the Editor — check `Window → Uplink` for status.
2. Register Uplink with your MCP client (see the documentation of your client).
3. Ask your assistant something like:

   > "Refresh Unity, fix any compile errors in PlayerController.cs, run the EditMode tests, and show me a screenshot of the Game view."

## How it works

```
AI client (Claude Code, Cursor, …)
        │  MCP protocol
        ▼
Uplink Editor plugin (this package)
        │  UnityEditor API, main thread
        ▼
Console · Compilation · Test Runner · Cameras
```

The Editor plugin marshals every command onto the Editor main thread, executes it against the `UnityEditor` APIs, and returns structured JSON (or PNG image content for screenshots). Long-running operations such as test runs and domain reloads are handled asynchronously and survive script recompilation.

## Recommended CLAUDE.md snippet

If you use Claude Code, add something like this to your project's `CLAUDE.md`:

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

Issues and PRs welcome. Keep tools small and focused — Uplink's goal is a tight feedback loop, not full Editor automation.

## License

MIT

---

*Uplink is not affiliated with Unity Technologies. "Unity" is a trademark of Unity Technologies.*
