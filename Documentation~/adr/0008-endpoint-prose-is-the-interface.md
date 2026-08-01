# ADR-0008 — An endpoint's prose is its interface

- **Status:** Accepted
- **Date:** 2026-08-01

## Context

Uplink's caller is a language model. It never reads the source, and it cannot experiment cheaply — a
misunderstood tool costs a wasted turn or, worse, a confident wrong conclusion about the project.

What the model actually sees depends on the adapter, and the difference is larger than it looks. An adapter
that generates one tool per operation shows it the `operationId` as the tool's name. [whispr](https://github.com/agxmeister/whispr)
does not: it lists endpoints as `summary ?? description`, returns `summary`, `description`, `parameters` and
`requestBody` on request, and never surfaces `operationId` at all.

So the fields that are always read are the prose ones, and under the adapter Uplink is developed against they
are the *only* ones.

## Decision

`summary` and `description` are treated as the tool's interface, not as documentation of it.

- `summary` is one line and says what the tool answers, because it may be all the model sees when choosing.
- `description` says how to use the result: that `compile` must be called again until `done`, that
  `read_console` takes a `nextSince` from the previous call, that `changed: false` means the errors reported
  were already standing. It describes behaviour a caller must know, never implementation.
- `operationId` stays a short verb — `status`, `read_console`, `set_play_mode` — because other adapters do use
  it as the tool name, and it costs nothing to keep good.
- Every parameter and every returned field is described. Two tests enforce this for each endpoint: one asserts
  that each field in a real response appears in the schema, the other that each parameter the handler reads is
  declared. `ApiSurfaceTests` additionally checks that every operation has a summary, a description of some
  substance, and an `operationId` no other operation uses.

## Consequences

- Endpoint classes carry more prose than code, which is the correct proportion for this package.
- Changing what a `Handle` returns without changing `Describe` fails the tests rather than silently shipping a
  tool the adapter cannot see.
- The descriptions embed the calling protocol from [ADR-0002](0002-self-polling-cycle.md), so an assistant
  gets the repeat-until-done cycle right without the project's `CLAUDE.md` having to teach it.
- The prose is tied to one audience. If the API ever acquires human callers as a first-class concern, the
  register will read oddly to them.
- Because whispr shows `summary` when choosing between endpoints, a vague summary is a real defect and not a
  style problem. It is worth writing them as answers to a question the assistant has.
