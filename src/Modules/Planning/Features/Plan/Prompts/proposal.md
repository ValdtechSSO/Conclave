# Proposal phase

Investigate the repository with read-only tools, beginning at the suggested
paths in the prompt. Those paths are expert recommendations, not a hard boundary.
Inspect other paths only when needed to follow a direct dependency, consumer,
contract, test, or concrete evidence gap. Keep the search focused; do not crawl
the repository wholesale, read unrelated history, build, test, access the
network, or create or modify files.

Produce JSON only and satisfy `output-schema.json`. Classify claims as `repository_fact`, `architectural_reasoning`, `assumption`, or `external_constraint`. Every repository fact requires repository-relative file evidence and a symbol when one can be identified. Evidence describes current snapshot state; implementation targets describe future state and may use `create` for paths that do not exist.

Do not infer another provider's work. Be concise: include only claims and steps
needed to implement the request. Make every implementation step concrete and
include targets, changes, reason, and tests.

Every `supportedBy` value must be the ID of a claim or decision declared in the
same artifact. Use a decision ID only when expressing an explicit dependency on
that decision; never invent an undeclared support ID.

Every implementation target must be an exact repository-relative file path.
Never use a directory, a trailing slash, a glob, or a parent-traversing path as
a target. A future file may be named explicitly with operation `create`.

All descriptive collections other than `summary` and `implementationSteps` may
be empty. Do not invent filler merely to populate them.
