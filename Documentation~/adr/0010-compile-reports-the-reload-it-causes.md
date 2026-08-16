# ADR-0010 — A finished compile reports the reload it caused

- **Status:** Accepted
- **Date:** 2026-08-16
- **Amends:** [ADR-0002](0002-self-polling-cycle.md) — the cycle's shape is unchanged; what `done` means and
  carries is not.

## Context

A long session driving `[InitializeOnLoadMethod]` setup scripts through Uplink showed that `compile`
answered a narrower question than the one being asked. The client
does not want to know "did the compiler finish"; it wants "did my Editor code run, and what did it say". Three
gaps stood between those questions:

1. **`done` arrived before the logs.** `Completed` was called at `compilationFinished`, which is *before* the
   domain reload a successful build causes — and the reload is what re-runs `[InitializeOnLoadMethod]`. A
   `/console` read straight after `done` could see nothing, and "this stage did nothing" was indistinguishable
   from "not yet".
2. **Reading the logs took bookkeeping.** Capturing `nextSince` before, reading `/console?since=N` after, and
   filtering out Uplink's own startup line: two extra round-trips and a piece of client state per stage.
3. **A no-op compile could not reload.** `changed: false` means no reload and no setup code run. The
   documented workaround was appending a newline to a `.cs` file to make Unity think it changed — dozens of
   file writes that had nothing to do with the change being made.

Job endpoints were rejected in ADR-0002 and nothing here reopens that; the question was only what the one
cycle should report, and when.

## Decision

**The run ends after its reload, not after its build.** `compilationFinished` with errors still closes the
run — no reload will follow a failed build. Without errors, the run marks a reload as promised
(`ExpectReload`) and stays `202`. On the far side of the reload, `Attach` recognizes the run
(`CrossedReload`) and closes it (`Reloaded`) after two quiet Editor ticks, so that `delayCall`-deferred work
gets to log first. Two graces bound the waiting: one concludes "nothing needed rebuilding" when no compile
starts, one stops waiting on a promised reload the Editor never delivers. Erring late is deliberate: a client
that polls absorbs an extra `202`, but is misled by an early `done`.

**The `done` result carries the run's console output.** `CompileLog` notes the console stream's position when
a run is asked for and, at hand-over, attaches everything logged since as a `console` field — the same page
shape `/console` returns, minus Uplink's own `[Uplink]`-prefixed chatter. The common case needs no `/console`
call and no `since` arithmetic at all.

**`{"force": true}` reloads even when nothing changed.** The trigger tick calls `AssetDatabase.Refresh()` as
before and then `EditorUtility.RequestScriptReload()`, which reloads the domain without recompiling — exactly
what re-running setup code needs, and cheaper than a forced rebuild. The result reports `forced` alongside
`changed`, so a forced reload and a real rebuild read differently.

**Every response says `isPlaying`, and a reload that ran during play mode carries a `note`.** Setup scripts
guard on play mode and silently do nothing; a `done` with `errors: 0` and an empty `console` is the symptom,
and nothing in that picture points at play mode unless the response does.

## Consequences

- Driving an N-stage setup script is N short poll loops against one endpoint: no file touches, no sleeps, no
  cursor bookkeeping, no shell.
- `durationMs` now spans build *and* reload, which is what the caller actually waited.
- A separate `/reload` endpoint was considered and rejected: it would be `compile` with one flag inverted, and
  every tool a model must choose between costs choosing accuracy (the ADR-0002 argument again).
- `CompileLog` now knows `IConsoleReader`. Both are Unity-free, the console never calls back into the compile
  log, and the alternative — the service stitching the page in after the fact — would have split the
  reporting of one result across two classes.
- The `console` page is capped like everything else (100 entries); its `truncated`/`nextSince` hand a client
  that wants more straight back to `/console`.
- One reload of history: a run started by an older Uplink and finished by this one reports its console page
  from position 0, because the older state had no cursor. It corrects itself on the next run.
