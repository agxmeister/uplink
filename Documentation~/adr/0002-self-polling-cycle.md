# ADR-0002 — Work that outlives a request is a self-polling cycle, not a job

- **Status:** Accepted
- **Date:** 2026-08-01

## Context

Three of the tools set off work that cannot finish inside the request that asks for it. Compiling scripts and
entering play mode both reload the Editor's script domain, which wipes every static and closes the HTTP
listener; a test suite simply takes longer than any sensible timeout. The request that starts such work can
therefore never be the request that reports it.

Three shapes were considered:

1. **Block and let it time out.** Least code. But the client never gets a clean pass or fail, and `504` stops
   meaning "the Editor is busy" — which the rest of the design depends on it meaning.
2. **Job endpoints.** `POST /compile` returns a job id, `GET /jobs/{id}` polls it. Uniform, and familiar. But
   `Route.Matches` is exact-equality only, so path templates would have to be added to the router; and every
   concern would grow a second tool beside it, doubling the surface a model has to reason about.
3. **A self-polling cycle.** One endpoint, called repeatedly.

## Decision

Each such endpoint runs a three-state cycle, its state in an `ISessionStore`:

```
idle      a call starts the work, becomes running, answers 202
running   a call reports progress and changes nothing, answers 202
done      a call hands the result over and returns to idle, answers 200
```

The result is delivered exactly once, so the call after it unambiguously means "do it again". `202` lets a
client tell "not finished" from "finished" without reading the body. `idle` is internal and never reported: a
call that finds it has already started the next run.

The cycle itself lives in a Unity-free class — `CompileLog`, `TestLog` — whose `Advance` returns both the
answer and whether the caller should now go and do the thing. The service beside it does the doing.

`set_play_mode` follows the same protocol but stores nothing, because the Editor itself records whether it is
playing: `PlayModeControl` compares what was asked for with what is, and asks again if they differ.

## Consequences

- No path templates, no job registry, no expiry policy for abandoned jobs. The session store's own lifetime
  handles the last of those.
- One tool per concern, so a model choosing between them has fewer to weigh.
- The model must be told to call again, which is done in each endpoint's `description` — see
  [ADR-0008](0008-endpoint-prose-is-the-interface.md).
- Only one run of a given kind can be in flight, which is the correct constraint anyway: the Editor cannot
  compile twice at once.
- Two calls that both find `done` cannot both get the result. A single assistant polling in a loop is the
  expected caller, so this is acceptable, but a second client watching the same Editor would see a run
  disappear.
- Work that reloads the domain must be deferred by one `EditorApplication.update` tick rather than done inside
  `Handle`, or the listener closes before the `202` can be written. `UnityCompiler` and `UnityPlayMode` both
  set a flag and act on the next tick.
