# ADR-0013 — A viewpoint of our own

- **Status:** Accepted
- **Date:** 2026-08-17
- **Amends:** [ADR-0005](0005-screenshots-render-a-camera.md) — screenshots still render a camera; what
  changes is that the camera need not be one the scene already has.
- **Reads against:** [ADR-0007](0007-scene-access-is-read-only.md) (scene access is read-only),
  [ADR-0008](0008-endpoint-prose-is-the-interface.md) (an endpoint's prose is its interface)
- **Implements:** REQ-0001 A

## Context

Uplink could photograph only the viewpoints a project happened to have cameras at. That is the common case
and it was enough for a long time, but it fails exactly where seeing matters most.

The reported case: the arkanoid menu is not UI but a small playable scene — two boards twenty world units
apart inside a slider, reached by steering a ball into an arrow-shaped collider. An arrow on the hall-of-fame
board was re-lettered from NEXT to PREV. `read_scene`, `read_object` and a `git diff` of the `.unity` file
all confirmed the change structurally. None of them could confirm the four-letter word cut into the banner,
which was the entire content of the change. `view=camera` photographed the title board; `view=scene`
photographed wherever a human had last left the Scene view — in that session, empty sky. The label shipped
unseen.

The geometry existed and, in play mode, was active. It was merely somewhere else.

## Decision

**`GET /screenshot?view=viewpoint` renders a camera Uplink creates for the shot**, positioned either
explicitly (`from`, with `at` or `dir`) or by fitting an object (`frame`, with `axis`).

**It goes on `/screenshot` rather than on a `/render` of its own.** One tool reuses `width`, `height`,
`crop`, `format` and `path` and their prose with no duplication, and `IViewCapture` stays a single seam. The
cost is real and was paid deliberately: the description grows a second personality and the parameter list
goes from seven to sixteen. It is paid down by giving the viewpoint its own labelled block in the prose
rather than by threading it through the existing sentences — the description is the interface (ADR-0008),
and a paragraph a reader can skip whole is cheaper than nine qualifications sprinkled through the rest.

**The camera is ours, hidden, and destroyed in a `finally`.** A `GameObject` with
`HideFlags.HideAndDontSave` carrying a `Camera` with `enabled = false` — so it never joins the render loop
and draws only when we ask — torn down beside the `targetTexture` and `RenderTexture.active` restores that
were already there. `HideAndDontSave` is what keeps this inside ADR-0007: the object is excluded from the
hierarchy the user sees, from saves, and from the dirty flag. That was verified live rather than assumed:
`sceneDirty` read `false` before and after shots in both edit and play mode, and `read_scene` shows nothing
afterwards.

**There is no fallback, in either direction.** ADR-0005's reasoning about a named `camera` applies verbatim:
a viewpoint is an explicit request, so a `frame` that names nothing, or a subtree with nothing to draw,
fails rather than quietly photographing the main camera. An answer that looked like agreement would be worse
than no answer — and here it would be worse still, because the caller asked precisely because the main
camera was not showing what it wanted.

**A subtree with no enabled renderers is a `400` that names inactivity as the likely cause.** That is the
exact state the arkanoid menu is in outside play mode, it is invisible from the client's side, and a bare
"nothing to render" would send the caller hunting. The message says so: *"The object or one of its ancestors
is probably inactive — in edit mode a subtree authored inactive renders nothing."*

**Rendering settings are copied from `Camera.main`** — clear flags, background colour, culling mask — so the
picture resembles what the game would draw rather than a differently-lit stand-in. With no main camera there
is nothing to resemble, so the honest default is solid black with every layer on. Whatever the rule, the
description states it: a screenshot that quietly differs from the game view is one that will be trusted
wrongly.

**The response echoes the pose actually used** — `from`, `at`, and `fov` or `ortho`. With `frame` the caller
did not choose those numbers and needs to know what it got, so a shot that framed badly can be nudged by
editing numbers rather than guessed at again. All four fields are nullable and omitted when absent, so a
camera, game or scene answer is byte-identical to what it was before viewpoints existed — a non-nullable
`fov` would have reported `fov: 0` in every one of them.

**Validation lives in the endpoint, not in the Unity class.** Every combination rule — `from` xor `frame`,
`at` xor `dir`, `fov` xor `ortho`, a viewpoint parameter given without `view=viewpoint` — is therefore
testable against a stub capture with no Editor involved, which is what keeps `IViewCapture` a seam rather
than a formality. `Arguments` grew `Float`, `Triple` and `Quad` so a malformed triple becomes a `400` in the
one place malformed inputs do; the crop's hand-rolled parsing, which was the standing exception to that
rule, was folded onto `Quad` at the same time.

## What was rejected

**Activating an inactive subtree so it can be photographed.** This is the tempting one — it would make
edit-mode menu shots work, and it is one line. It is refused because `SetActive` in edit mode dirties the
scene with **no way to un-dirty it**, so an unrelated later save would persist a change nobody asked for.
That is precisely the class of accident the arkanoid setup script carries repair stages for, and Uplink
causing it would be worse than Uplink failing to take a picture. If it is ever wanted it belongs behind an
explicit parameter, restricted to play mode where the change dies at stop, reported in the response, and
argued out against ADR-0007 in its own record.

It is also likely to stay unnecessary. Once input can be injected (REQ-0001 B), a game that can be driven
can put its own objects on screen — which is both a better picture and a truer one, since it shows a state
the game actually reaches.

**`frame` taking several comma-separated paths, fitting their union.** One path was enough for the reported
case. Making it plural later is additive, and the union-of-bounds code is already the shape it would need,
so nothing here forecloses it.

## Consequences

- Anything standing in the open scenes can be photographed, whatever the project's cameras are pointed at.
  The reported case now works in one call: `frame=/MenuScreen/MenuSlider/MenuHall` returns a shot in which
  the plaque's two lines and both arrows are legible.
- `screenshot` is a sixteen-parameter tool. That is a lot for a model to read, and the block structure of
  the description is the only thing holding it together; a seventeenth parameter should be a prompt to
  reconsider the split, not a reflex.
- The fit is a distance that satisfies both axes — `max(halfH/tan(fov/2), halfW/(tan(fov/2)·aspect))` with a
  margin, plus the object's own depth — so a wide object in a tall image is limited by width, as it should
  be. `near` and `far` are derived from that distance rather than fixed, because a fixed pair cannot contain
  a framed object two hundred units wide and still resolve depth on one a centimetre across. A `from`
  viewpoint says nothing about how big the scene is, so its range is deliberately generous and `near`/`far`
  are there for when it is not enough.
- Bounds come from `Renderer.bounds`, so what is fitted is what is drawn — colliders, empty transforms and
  disabled renderers are not in the frame and were never going to be in the picture.
- Gizmos, overlays and handles are still absent, as they are for `view=camera`. Same limitation, same
  documented answer: `view=game` in play mode.
- `UnitySceneProbe`'s path walk moved to `Hierarchy/ObjectPath` so the capture can use it. The comment that
  matters travelled with it — deliberately not `GameObject.Find`, which cannot see inactive objects — and
  that is exactly why `frame` can resolve an inactive subtree and then report inactivity as the cause,
  rather than reporting no such object and sending the caller after a typo that is not there.
