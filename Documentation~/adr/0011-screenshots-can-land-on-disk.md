# ADR-0011 — Screenshots can land on disk, and be cropped where they are rendered

- **Status:** Accepted
- **Date:** 2026-08-16
- **Amends:** [ADR-0005](0005-screenshots-render-a-camera.md) — capture and defaults are unchanged; a third
  way to return the image is added.

## Context

ADR-0005 weighed raw PNG against base64-in-JSON and chose base64, because the whispr adapter decodes every
response as text and mangles binary. That reasoning still holds — for the transport. But it left the client
unable to *look* at the picture: an assistant reads images from files, not from base64 inside a tool result,
so every visual check became `curl -o shot.png` plus platform image tools to crop details out of a large
render. Because looking was awkward, looking happened late — and late is exactly when a screenshot revealing
a wrong design is most expensive.

There was a third option ADR-0005 did not consider: do not send the image at all.

## Decision

**`?path=/tmp/shot.png` writes the PNG to that file and answers `{path, view, width, height}`.** The Editor
and the client share a filesystem — the API binds loopback only, so they are the same machine by
construction — and a path crosses any adapter intact. `path` overrides `format`; failures to write are the
path's fault and answer `400` naming it. Parent directories are created, because "capture and look" should be
one call even into a fresh scratch directory.

**`?crop=x,y,width,height` keeps a region of the rendered image.** Coordinates count from the top-left corner
the way every image tool counts, and are converted to texture space (bottom-left) inside `UnityViewCapture`,
which crops the pixels before encoding. Rendering at 4K and cropping to the few hundred pixels under
inspection is how small lettering gets checked without any client-side tooling — and without shipping the
other eight million pixels.

**Base64 stays the default.** This is an addition; ADR-0005's trade-off for the inline case is untouched.

## Consequences

- Capturing and viewing a detail of the scene is one call and one file read; the shell and `sips` drop out of
  the loop entirely.
- The JSON answer has one shape with two optional fields: `image` when the bytes travel inline, `path` when
  they did not. Nothing binary ever crosses the transport in the `path` case, which sidesteps the adapter
  question rather than answering it.
- `width` and `height` report the image actually produced — the crop's size, when one was asked for.
- Writing to a client-chosen path is a local-file write on behalf of an unauthenticated loopback caller. That
  is the same trust model as the rest of the API (see the no-authentication note in `CLAUDE.md`), but it is
  the first endpoint that writes outside the Editor's own project, and worth remembering if authentication is
  ever revisited.
