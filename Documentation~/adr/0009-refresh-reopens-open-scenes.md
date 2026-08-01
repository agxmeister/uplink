# ADR-0009 — Refresh re-opens the open scenes

- **Status:** Accepted
- **Date:** 2026-08-01

## Context

[ADR-0007](0007-scene-access-is-read-only.md) settled that the assistant edits scenes "through its own file
tools, or by writing code", and that Uplink only reports what happened. Writing a `.unity` file is therefore
the sanctioned way to author scene content — but until now it did not work, and the reason was invisible from
the outside.

Two separate mechanisms defeat it.

An Editor that is not the focused window defers importing entirely. It has no reason to poll the file system,
so a file written by another process is picked up when someone next clicks on the Editor — which, for a
headless caller driving it over HTTP, may be never. Every `compile` in that state reports `changed: false`,
and the project appears simply not to have been edited.

Worse, importing does not help a scene that is already open. Unity holds its own copy of an open scene and
does not re-read the file; a domain reload restores that copy rather than loading from disk. So an edit
written into a `.unity` can survive any number of imports and compiles without ever appearing, and
`read_scene` — correctly — keeps reporting a hierarchy that no longer matches the file. The two disagree, and
nothing in the API said so.

Working around this in the caller means writing an Editor script into the project and finding a way to make it
run, which is a workaround for a missing tool rather than a use of the API.

## Decision

`refresh` imports changed files and, unless told otherwise, re-opens the open scenes from their files.

Re-opening is the only mechanism that makes a scene file's contents current: `EditorSceneManager.OpenScene`
on an already-open path. It discards whatever the Editor was holding — including the selection and the undo
history — because Unity's own prompt comes from `SaveCurrentModifiedScenesIfUserWantsTo`, which the Editor UI
calls first and which Uplink must never call. A modal dialog raised by an HTTP request hangs the Editor with
nobody present to dismiss it, and that is precisely the failure this endpoint exists to end.

Guarding the caller's unsaved work therefore falls to the endpoint, not to Unity:

- A dirty open scene is answered `409` and nothing is reloaded.
- `discardUnsavedChanges: true` is the caller stating that losing it is acceptable.
- `scenes: false` imports only, which is unaffected by dirtiness and is the right call when no scene file
  was written.

This does not widen [ADR-0007](0007-scene-access-is-read-only.md). Uplink still authors no scene content: it
changes which bytes the Editor is looking at, never what those bytes say. That ADR's "no dirty-scene handling"
consequence is what this record supersedes — reading a scene needs none, re-opening one cannot avoid it.

The refresh follows the [self-polling cycle](0002-self-polling-cycle.md), because importing changed scripts
reloads the domain and takes the listener with it.

## Consequences

- Writing scene YAML becomes a workable way to author scenes, which is what ADR-0007 already assumed.
- `rootCount` per scene is returned so a caller can confirm that a reload picked up what it wrote, without a
  second `read_scene`.
- Selection and undo history are lost on every reload. For an assistant-driven Editor this is a fair price;
  for a human working in the same Editor it is not, which is what the `409` is protecting.
- Additive scene setups are rebuilt: the first scene re-opens `Single`, the rest `Additive`, and the active
  scene is restored. A scene that has never been saved has no file to re-read and is left alone.
- The endpoint refuses to run in play mode, where neither importing nor re-opening is safe.
- `compile` keeps its own `AssetDatabase.Refresh`, so a caller that only edited scripts still needs just the
  one call.
