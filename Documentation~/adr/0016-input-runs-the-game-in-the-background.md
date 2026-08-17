# ADR-0016 — Playing input turns on `runInBackground`

- **Status:** Accepted
- **Date:** 2026-08-17
- **Reads against:** [ADR-0007](0007-scene-access-is-read-only.md) — Uplink does not modify the project. This
  changes something, and the whole argument is about *what* it changes and for how long.
- **Implements:** REQ-0001 B

## Context

`POST /input` was written, was correct, and did nothing at all.

Measured against the arkanoid project with the Editor running but not the foreground application — which is
the *normal* condition when an assistant is driving it, since the assistant lives in another window:

```
frame=1  left=False  appActive=False   ... every tick, for a 1.5s hold
frame=1  ...                                Time.frameCount never advanced
```

The events were queued correctly. The game never ran. `Time.frameCount` stayed at **1** for the whole
script, because a Unity player does not tick while the application is in the background unless
`runInBackground` is on, and the arkanoid project — like most projects, since it is the default — has
`runInBackground: 0` in its Player settings. `Update` never ran, so `Paddle.Update` never read the key that
was sitting there pressed and waiting for it.

The feature's entire premise is reaching states that need play. A tool that only works while a human is
looking at the Game view is not that tool.

## Decision

**Starting a script sets `Application.runInBackground = true`, and the endpoint's description says so.**

It is the **runtime property**, not the Player setting, and that distinction carries the whole argument:

- `PlayerSettings.runInBackground` is a project asset. Writing it would dirty `ProjectSettings.asset`,
  outlive the session, change how the project's own builds behave, and show up in someone's `git status`.
  That is squarely what ADR-0007 refuses.
- `Application.runInBackground` is a property of the running player. It is reset when play mode ends, it
  touches nothing on disk, and it is invisible to source control. It dies exactly the way a play-mode change
  is supposed to die.

It is set on the call that **starts** a script, not on load and not on every poll, so an Editor that is never
sent any input is never touched at all.

**It is not restored when the script finishes.** Restoring it mid-session would re-freeze the game between
scripts, so the very next `screenshot` would photograph a stalled frame and `read_scene` would report a world
that had stopped moving. Play mode ending is what restores it, which is the correct scope: the change lasts
as long as the session it was made for.

## What was rejected

**Changing `editorInputBehaviorInPlayMode`.** While diagnosing the above, the Input System's
`PointersAndKeyboardsRespectGameViewFocus` setting looked like the culprit and its alternative,
`AllDeviceInputAlwaysGoesToGameView`, looked like the fix. It was not the culprit — with the player loop
actually running, `InputSystem.QueueStateEvent` reaches the game perfectly well with the Editor in the
background and the Game view unfocused, which was confirmed by watching the paddle move. And it would have
been a bad fix regardless: it is a project setting, it persists, and it would silently change what happens
when the developer types in the Console during play mode.

**Requiring the human to focus the Game view.** That was the first theory and it was wrong, but it is worth
recording that it would also have been unacceptable: a feature whose purpose is unattended driving cannot
require attendance.

## Consequences

- `play_input` works with the Editor minimised, behind a browser, or on another desktop — which is the
  condition it will actually be used in.
- The game runs in the background *after* a script as well as during it, until play mode ends. For most
  projects that is an improvement, since `screenshot` and `read_scene` then observe a game that is still
  running rather than a frozen frame. A project that deliberately pauses itself in the background will
  behave differently under Uplink than under a human, and the description says so rather than leaving it to
  be discovered.
- Frame rate in the background is throttled by the Editor — roughly 10fps was observed — so a script's
  wall-clock timing is honoured (the schedule runs on `EditorApplication.timeSinceStartup`, not on frames)
  but the game gets fewer frames in which to react. A hold of 0.05s is the default for exactly this reason:
  it must span a frame at a background frame rate, not at 60fps.
- The diagnosis cost more than the fix, and two measurements were wrong before the right one: `/Paddle` was
  read while it was inactive (the menu uses a different paddle), and `Keyboard.current.isPressed` was read
  from `EditorApplication.update`, which sees the *editor* state buffer rather than the player's. Both read
  as "input is blocked" when input was fine. The lesson worth keeping: **verify against something the game
  itself moved** — a transform the game writes — rather than against the input system's own state as seen
  from the wrong side.
