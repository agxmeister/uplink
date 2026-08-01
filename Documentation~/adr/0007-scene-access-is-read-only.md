# ADR-0007 — Scene access is read-only

- **Status:** Accepted
- **Date:** 2026-08-01

## Context

An assistant that has just changed a script often needs to know whether the change reached the object it meant.
A screenshot answers "does it look right" but not "is `mass` actually 1.5 on the Player's Rigidbody".

Reading the scene raises two questions: how to express arbitrary user components without knowing their types,
and whether to allow writing them back.

## Decision

`read_scene` walks the open scenes; `read_object` returns one GameObject's components and their values. Both
are read-only. Nothing in Uplink modifies a scene.

Values come from iterating a `SerializedObject`, not from reflecting over the component's own type. Unity has
already decided which fields are worth showing, and this shows the same ones the Inspector does — for any
script in any project, with no knowledge of it.

Objects are identified by their slash-separated path, because instance ids mean nothing across a domain
reload. The walk finds inactive objects too, which `GameObject.Find` cannot — and an object being inactive is
often exactly the thing being looked into.

## Consequences

- The assistant edits scenes the way it edits everything else: through its own file tools, or by writing code.
  Uplink tells it what happened. That keeps the package a feedback channel rather than a remote control, and
  keeps a confused model from silently rewriting a scene.
- No undo integration, no dirty-scene handling, no serialization races to get wrong.
- Values that JSON cannot carry — animation curves, nested structures, arrays — are reported as their type
  and size rather than dropped, so a client can see that something is there without this having to model
  every shape Unity can serialize.
- Only top-level serialized properties are returned. A deep component would otherwise flatten into hundreds
  of entries.
- The walk stops after 2000 objects and at the requested depth, and says `truncated` when it did. A
  production scene is far larger than any useful answer; `path` narrows it to a subtree.
- Two objects with the same path in the same scene are indistinguishable. Unity permits it; this reports the
  first found.
- Writing is on the roadmap. When it arrives it will be a separate endpoint with its own record here, not a
  quiet widening of these two.
