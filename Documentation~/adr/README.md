# Architecture decisions

The decisions that shaped Uplink, one per file, newest number last. Each records what was chosen, what it was
chosen *over*, and what it costs — so that a later reader can tell a considered constraint from an accident,
and so that reversing one is a decision rather than an oversight.

A decision belongs here when it would be expensive to reverse, when it explains something that otherwise looks
arbitrary, or when a reasonable contributor would want to argue with it. Ordinary implementation choices do
not; those live in the code and in [`CLAUDE.md`](../../CLAUDE.md).

Records are immutable. When one stops being true, add a new record that supersedes it and mark the old one
*Superseded by ADR-nnnn* rather than editing it — the reasoning that has been overtaken is usually the most
useful part.

| # | Decision | Status |
|---|---|---|
| [0001](0001-rest-api-described-by-openapi.md) | A REST API described by OpenAPI, with MCP left to an adapter | Accepted |
| [0002](0002-self-polling-cycle.md) | Work that outlives a request is a self-polling cycle, not a job | Accepted |
| [0003](0003-session-state-and-services.md) | State crosses a domain reload in `SessionState`, gathered by services | Accepted |
| [0004](0004-console-collector-seeded-from-the-editor.md) | Uplink collects console messages itself, seeded once from the Editor's | Accepted |
| [0005](0005-screenshots-render-a-camera.md) | Screenshots render a camera, and are base64 by default | Accepted |
| [0006](0006-test-framework-is-a-hard-dependency.md) | The Unity Test Framework is a hard package dependency | Accepted |
| [0007](0007-scene-access-is-read-only.md) | Scene access is read-only | Accepted |
| [0008](0008-endpoint-prose-is-the-interface.md) | An endpoint's prose is its interface | Accepted |

## Why `Documentation~`

Unity's asset database imports every folder in a package and generates a `.meta` file for every file in it.
A trailing `~` is the one thing it ignores, so documentation lives here rather than in `docs/` and costs the
consuming project nothing.
