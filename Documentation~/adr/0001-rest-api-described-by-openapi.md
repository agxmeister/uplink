# ADR-0001 — A REST API described by OpenAPI, with MCP left to an adapter

- **Status:** Accepted
- **Date:** 2026-08-01

## Context

Uplink exists to let an AI assistant see what its changes did to a Unity project. The assistant reaches its
tools over MCP, so the obvious implementation is an MCP server inside the Editor.

That would mean carrying an MCP transport, a session model and a protocol version inside an Editor plugin —
in C# against a moving specification, on Unity's older language level, with a domain reload liable to tear the
whole thing down mid-session. It would also make the package unusable from anything that is not an MCP client,
including `curl` while debugging it.

## Decision

Uplink is a plain REST API that describes itself at `GET /openapi.json`. It contains no MCP code. An
off-the-shelf OpenAPI-to-MCP adapter turns the description into tools.

The description is *derived*: `Router` and `OpenApiEndpoint` read the same `IEndpoint` collection, so a live
route and its published description cannot drift apart. An endpoint owns its own method, path, description and
behaviour, so adding a tool never means editing the router or the spec.

The adapter this is developed against is [whispr](https://github.com/agxmeister/whispr), configured with an
edge pointing at the running Editor.

## Consequences

- Every tool is reachable with `curl`, which makes the package debuggable without an AI client in the loop.
- The protocol churn of MCP is somebody else's problem, and a different adapter can be swapped in.
- The package cannot rely on MCP features that have no OpenAPI expression — progress notifications, sampling,
  resources. Anything of that kind has to be modelled as ordinary HTTP; see [ADR-0002](0002-self-polling-cycle.md).
- Adapters vary in what they do with a specification, and the difference is not cosmetic. Whispr does not
  generate one tool per operation: it exposes `<edge>-get-api-endpoints`,
  `<edge>-get-api-endpoint-details` and `<edge>-call-api-endpoint`, and the model chooses an endpoint from
  the `summary` each one reports. `operationId` is never shown to it. That shapes how endpoints must be
  written — see [ADR-0008](0008-endpoint-prose-is-the-interface.md) — and how they must answer, see
  [ADR-0005](0005-screenshots-render-a-camera.md).
- Whispr's read-only profile filters the specification down to `GET`, which hides `compile`, `run_tests` and
  `set_play_mode`. The feedback loop needs those, so the Uplink edge must not be configured read-only.
- There is no authentication. The listener binds loopback only, but any local process can reach it. Acceptable
  for a development tool; worth revisiting if a mutating endpoint ever does something destructive.
