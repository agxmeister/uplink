# ADR-0005 — Screenshots render a camera, and are base64 by default

- **Status:** Accepted, amended by [ADR-0011](0011-screenshots-can-land-on-disk.md)
- **Date:** 2026-08-01

## Context

Two questions, and they turned out to be linked.

**What to capture.** Grabbing the Game view is what a person would call a screenshot, and it includes
everything drawn over the camera. But it needs the window open and rendering, its size is whatever the window
happens to be, and outside play mode it comes back empty or stale. Rendering a camera through a
`RenderTexture` of our own works in edit mode, at whatever size is asked for, with no window open or focused —
but it is not pixel-identical to what a person sees.

**How to return it.** `Response` already carries bytes, so `image/png` is the natural answer. Whether it
survives the trip to a model is another matter, and that depends entirely on the adapter.

Reading [whispr](https://github.com/agxmeister/whispr) settled the second question. `Rest.callEndpoint` calls
axios with no `responseType`, so the default of `json` applies: a PNG body is decoded as UTF-8 text, fails to
parse, and is handed back as a mangled string inside `{status, body}`. Raw PNG does not survive it.

## Decision

**Capture:** `view=camera` is the default and renders the scene's main camera through a `RenderTexture`.
`view=scene` renders the Scene view's camera the same way. `view=game` grabs the Game view with
`ScreenCapture.CaptureScreenshotAsTexture`, but only while play mode is running.

A view that cannot draw falls back to one that can, **in both directions**: a scene with no enabled camera is
captured from the Scene view, and a closed Scene view from a camera. The response always reports which view
was *really* rendered, in the `view` field and the `X-Uplink-View` header, so a fallback is never silent.

Naming a `camera` is the exception. That is an explicit request, so a camera that is missing or disabled fails
the call rather than quietly photographing a different one — an answer that looked like agreement would be
worse than no answer.

**Encoding:** `format=base64` is the default, returning `{view, width, height, image}` as JSON.
`format=png` returns the PNG itself, which is what a browser or `curl -o` wants.

## Consequences

- The dependable path is the default one: a camera render needs no window, no focus and no play mode, so
  `screenshot` works on a project the moment it is opened.
- The default answer is roughly a third larger than the image, and the model reads it as text. That is the
  price of it arriving intact, and it is the right trade — a corrupted screenshot is worse than a verbose one.
- `curl -o shot.png "…/screenshot?format=png"` still does the obvious thing, so debugging by hand is unchanged.
- A camera render does not show gizmos, overlays, or anything else drawn on top of the camera. When that
  matters, `view=game` in play mode is the honest answer, and it says so when it could not oblige.
- Asking for a picture nearly always yields one, which is the point: an empty scene, a project with no
  `MainCamera` tag, and a disabled camera are all ordinary states an assistant will meet, and none of them
  should turn "show me the scene" into an error. The reciprocal risk — being handed the wrong view without
  noticing — is what `view` and `X-Uplink-View` are for.
- `Camera.main` and `Camera.allCameras` both see only *enabled* cameras on *active* objects, so a camera
  that exists but is switched off reads as no camera at all. The error message says so, because the
  difference is invisible from outside the Editor.
- The camera is the scene's, not ours, so `targetTexture` and `RenderTexture.active` are restored in a
  `finally` — leaving a scene camera pointed at a texture about to be destroyed would black out the Editor's
  own view of it.
- If a future adapter passes binary through cleanly, flipping the default back is one line. The endpoint
  answers in both forms and describes both, so nothing else has to change.
