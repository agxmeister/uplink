# ADR-0003 — State crosses a domain reload in `SessionState`, gathered by services

- **Status:** Accepted
- **Date:** 2026-08-01

## Context

[ADR-0002](0002-self-polling-cycle.md) needs a run's state to survive the domain reload that the run itself
causes. Two other things need the same: console messages arrive while nobody is asking, and compiler messages
arrive per assembly with the reload landing somewhere among them.

Statics do not survive. `EditorPrefs` does, but it survives too much — a compile result left over from
yesterday's Editor session would be reported as today's. A file under `Library/` would work but adds
serialization, paths and cleanup for something that should not outlive the session anyway.

Endpoints alone are also not enough. An endpoint only answers; something has to be listening to the Editor
*before* a request arrives, because a log message is gone by the time anyone asks for it.

## Decision

Two things, used together.

**`ISessionStore`**, backed by `UnityEditor.SessionState`. Its lifetime is exactly right: it survives a domain
reload and is cleared when the Editor closes. It holds strings, so `Stored.Read`/`Stored.Write` put JSON
through it, and a value that fails to parse is treated as absent rather than as an error — nothing kept there
is precious enough to fail a request over.

**`IUplinkService`**, with `Attach` and `Detach`. `Uplink` attaches every service when the domain loads and
detaches it before the domain goes away, which is also each service's chance to hand its state to the store and
pick it up on the other side. A service that fails to attach is logged and skipped; the Editor is still worth
talking to without one collector.

Each service is paired with a Unity-free class that holds the actual state and logic — `ConsoleBuffer`,
`CompileLog`, `TestLog` — so only the thin service touches `UnityEditor`.

## Consequences

- A domain reload becomes something a test can express: capture one log's state through an `InMemoryStore`,
  restore it into a fresh instance, and carry on. Every reload-crossing path is covered this way.
- Results are written as they arrive rather than collected at the end, because the reload lands mid-sequence.
  That is a write per compiler message and per finished test — cheap, and the alternative is losing them.
- Anything left mid-flight by a reload nobody told us about would report `running` forever, so `UnityCompiler`
  recovers on attach: a run in progress with no compilation actually happening is marked finished with
  whatever was recorded.
- Closing the Editor discards everything, which is correct. Nothing here describes the project; it all
  describes one session's work.
- `SessionState` is global to the Editor, so keys are prefixed. Two Unity instances have separate ones.
