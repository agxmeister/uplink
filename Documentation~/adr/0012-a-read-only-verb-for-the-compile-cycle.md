# ADR-0012 — A read-only verb for the compile cycle

- **Status:** Accepted
- **Date:** 2026-08-16
- **Amends:** [ADR-0002](0002-self-polling-cycle.md) — the cycle keeps its shape and its one-shot hand-over;
  what changes is that a second verb may look at it.

## Context

`POST /compile` means two things at once: *start a run* and *poll the one already going*. That overload is
what makes the cycle work with no job ids and no second endpoint (ADR-0002), and it is also its sharpest
edge:

1. **One poll too many starts a build.** The result is handed over exactly once, so the call after `done`
   unambiguously means "again". A retry, a race or a second watcher therefore compiles — and nothing in the
   response distinguishes "your result" from "a run you just started by asking".
2. **The verb everyone tries first was refused.** `GET /compile` answered `405`. A driving session against
   the arkanoid project lost ninety seconds to exactly this: a poll loop that checked for `200`, read the
   `405` as "still waiting", and only then corrected itself to `POST`. The `405` body said the right thing;
   the loop was never going to read it.

Both are the same complaint from two directions — observing and acting are one call, and only one of them is
safe to repeat.

## Decision

**`GET /compile` observes the cycle; `POST /compile` drives it.** One path, two operations, which an adapter
lists as one tool with two modes rather than as two tools — the ADR-0002 argument about tool count still
holds, and `Router` and `OpenApiEndpoint` already key on method *and* path, so both were picked up with no
edits to either.

The read never mutates: `CompileLog.Observe` takes the same lock and builds the same report as `Advance`, but
starts nothing and consumes nothing. `ICompiler` grows `Peek` beside `Poll`, and `UnityCompiler.Peek`
deliberately does not even persist — nothing changed, so what is stored still describes the cycle.

**The observer sees a third state.** `Advance` reports only `compiling` and `done`, because `idle` is a
resting place a caller that acts never finds itself in — a call that lands on it has already started the next
run. To a caller that only looks, `idle` is the answer to the question that matters: *would the next `POST`
hand a result over, or build?* So `GET` reports `idle`, `compiling` (`202`) and `done` (`200`), where `done`
means "a finished result is waiting for a `POST` to take delivery", and `idle` carries whatever the last run
left standing — its errors, its `durationMs` — because those are still the truth about the project.

**Everything `GET` returns is marked `stale: true`.** The one-shot hand-over is `POST`'s and stays `POST`'s;
a result read here can be read again, and while `state` is `done` it has not been delivered to anyone yet.
Progress reports (`compiling`) carry no mark: they are live, not somebody else's result. The `console` page
rides along on a `done` observation, as it does on the hand-over, but not on an `idle` one — once the cycle
is at rest the window since the run began has filled with unrelated output, and `read_console` is the honest
way to ask.

**A body-less `POST` stays a `411`.** Mono's `HttpListener` rejects a `POST` with no `Content-Length` while
parsing the request, before any code in this repository runs; that was documented as unfixable-here in the
last round, and it still is. What changed is that the `compile` description now says so itself — a model that
reads the prose sends `{}` — and that the verb a bare `curl` reaches for, `GET`, now works.

## Consequences

- A poll loop of only `GET`s is safe by construction: ten of them leave the Editor exactly as they found it.
  The recommended shape is `POST` to start, `GET` to wait, `POST` to collect.
- Both verbs answer `202` while a run is going, so a client can switch between them mid-wait without reading
  the body to tell "not finished" from "finished".
- The two operations describe one payload from one builder — `CompileEndpoint.ResultSchema(observed)` — so
  they cannot drift into describing the same JSON differently. The `observed` flag is the only difference:
  the wider `state` enum and `stale`.
- `read-only profile` adapters (whispr's, for one) now see something of `compile`: the status read, but not
  the ability to build. That is the correct half to expose to a read-only client.
- `/tests`, `/play` and `/refresh` share the cycle and did not get the same treatment. The same split fits
  them, and the shape to copy is here — `Observe` beside `Advance`, `Peek` beside `Poll` — but the hazard was
  reported against `compile`, which is polled far more than the rest, and speculative symmetry costs a tool
  surface that nobody has yet asked for.
