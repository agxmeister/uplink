# ADR-0004 — Uplink collects console messages itself, seeded once from the Editor's

- **Status:** Accepted
- **Date:** 2026-08-01

## Context

`read_console` has to answer "what did the Editor say". There are two sources.

`Application.logMessageReceivedThreaded` is public, stable across Unity versions, and testable — but it only
hears what is logged after Uplink subscribes, so everything from before the package loaded is invisible.

`UnityEditor.LogEntries` is what the Console window itself reads. It has the full history, the compile and
import errors, and the correct grouping — and it is internal, reachable only by reflection, and free to change
in any Unity release.

## Decision

Both, with the collector as the source of truth.

`ConsoleCollector` subscribes to `logMessageReceivedThreaded` and appends to a capped `ConsoleBuffer`, which
goes through the session store on each domain reload so numbering continues across it.

`UnityConsoleHistory` reflects into `LogEntries` **once**, on the session's first load, to seed the buffer with
what came before. Seeding only there is what keeps a message from being recorded twice. Every reflective call
is wrapped so that a renamed field costs the package its history and nothing else, and the response reports
`historyAvailable` so a client knows which it got.

Each message carries a monotonic `seq`, and every response carries `nextSince`. A client reads `nextSince`,
does something, and asks again — and gets exactly what that action produced.

The threaded callback is deliberate: Unity delivers a message on whichever thread logged it, and ignoring the
ones off the main thread would lose exactly the failures worth seeing.

## Consequences

- The common case — "what did my change just log" — never touches the reflective path at all.
- A Unity upgrade that breaks `LogEntries` degrades history rather than breaking the tool.
- Messages recovered from the Console have no timestamp and cannot be split into message and stack trace,
  because the Console stores them as one blob and does not say where the split is. They are reported whole,
  and `time` is absent on them.
- The buffer is capped at 1000 entries; `seq` keeps counting past what was dropped, so a cursor stays valid
  even when the messages it pointed at are gone.
- Uplink does not clear the Console. Clearing is what a person does to make the window readable, and the
  cursor makes it unnecessary for a client.
