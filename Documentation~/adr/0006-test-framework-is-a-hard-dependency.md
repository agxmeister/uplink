# ADR-0006 — The Unity Test Framework is a hard package dependency

- **Status:** Accepted
- **Date:** 2026-08-01

## Context

`run_tests` drives `TestRunnerApi`, which lives in `com.unity.test-framework`. Uplink's other six tools need
nothing of the sort, so the question was whether a project without that package should still get the rest.

Making it optional is not a matter of an `#if`. An assembly definition cannot reference an assembly that is
not there, so the Unity-facing runner would have to move into its own asmdef, guarded by `versionDefines` and
`defineConstraints` so the whole assembly is skipped when the package is absent. That assembly could then not
be referenced *from* `Uplink`, because the dependency points the wrong way — so the optional assembly would
have to register its own service and endpoint into the composition root.

That inverts the rule that `Uplink` is the only place naming concrete types, and it is the rule that keeps the
wiring readable.

## Decision

`com.unity.test-framework` is declared in `package.json` `dependencies`, and both assembly definitions
reference `UnityEditor.TestRunner` and `UnityEngine.TestRunner` directly. The Package Manager installs it with
Uplink, so a project without it does not arise.

## Consequences

- The composition root stays the single place that names concrete types, and registration stays one line per
  tool.
- Uplink pulls a package into projects that may not have wanted one. It is small, first-party, and something
  most Unity projects have already; and a project that installs a tool whose stated purpose includes running
  tests has few grounds to object.
- The version floor is `1.1.33`, low enough to be satisfied by anything from Unity 2021.3 onwards.
- Reversing this is contained: the guarded-assembly design above is still available, at the cost of a
  registration hook on `Uplink`.
- A PlayMode run reloads the domain in the middle of itself, unregistering every callback. `UnityTestRunner`
  registers them on every `Attach`, not only when a run starts — without that, the second half of such a run
  reports to nobody and the results come back silently short. This is the subtlest thing in the package.
