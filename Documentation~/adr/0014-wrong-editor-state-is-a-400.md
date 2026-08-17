# ADR-0014 — A right request in the wrong Editor state is a `400`

- **Status:** Accepted
- **Date:** 2026-08-17
- **Settles:** REQ-0001 open question 4

## Context

Endpoints are growing preconditions. `POST /input` needs play mode, because outside it there is no player
loop to receive events. `pause` and `step` already need it. More will follow, and each one raises the same
question: when the request is well-formed but the Editor is not in a state where it means anything, what
status does the client get?

`409 Conflict` is the more literal answer, and the case for it is real — "your request is fine, the resource
is not in a state that permits it" is precisely what `409` was minted for, and a client could branch on it
without reading prose.

`400` is what the codebase already does. `FaultBarrier` knows exactly three outcomes — `400` from
`BadRequestException`, `504` from `TimeoutException`, `500` from anything else — and `Response.Error` gives
all of them one shape, `{"error": "..."}`.

Deciding this once is cheaper than deciding it per endpoint, and deciding it late is worse than deciding it
early: a mixed convention is the one outcome with no defenders.

## Decision

**`400`, through the existing `BadRequestException`, and the message carries the meaning.**

> `"Not in play mode. Input needs a running player loop — call set_play_mode first."`

Three things settled it.

**Nothing in the chain reads the distinction.** whispr — the adapter Uplink is developed against, and the
reason ADR-0008 says the prose is the interface — hands the response body to the model. A model that reads
*"call set_play_mode first"* does the right thing; a model that reads `409` looks for prose to tell it what
`409` meant here. The status code would be a distinction drawn for a reader that does not exist.

**It would cost a second failure path in the barrier.** `FaultBarrier`'s three outcomes are three because
each means something different to a *client*: retry (`504`), fix your request (`400`), report a bug (`500`).
A `ConflictException` would add a fourth path whose client advice — fix the Editor's state, then retry — is
`400`'s advice with a different noun.

**The precondition is usually the caller's mistake anyway.** An input script sent outside play mode is a
sequencing error in the caller, the same class of thing as a malformed step. Treating "you sent this too
early" and "you sent this wrong" alike is defensible in a way that treating a timeout as a bug would not be.

## Consequences

- `400` now covers two things: a request that is malformed, and a request that is well-formed but premature.
  The message is what tells them apart, so a precondition message must say **what state is needed and which
  tool gets there** — naming the tool, not just the state. *"Not in play mode"* alone would be a worse
  message than the old one it replaced.
- A client cannot branch on status alone to decide "retry after changing state" versus "fix the request".
  Accepted: no client in the chain wants to, and the retry advice `504` carries is the only branch that has
  ever been asked for.
- If a mutating endpoint ever needs a genuine optimistic-concurrency conflict — two writers, one resource —
  that is a different question from this one and deserves its own record rather than this one's precedent.
- `ArgumentNullException` guards and other programmer errors keep falling through to `500`. This decision is
  about the Editor's state, not about Uplink's bugs, and blurring the two would cost the one distinction
  `FaultBarrier` exists to draw.
