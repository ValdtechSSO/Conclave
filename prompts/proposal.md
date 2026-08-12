# Proposal phase

Inspect the repository snapshot yourself. Read relevant source, tests, architecture, domain documents, and Git history when useful. You may build, test, and create scratch files only in this disposable workspace.

Produce JSON only and satisfy `output-schema.json`. Classify claims as `repository_fact`, `architectural_reasoning`, `assumption`, or `external_constraint`. Every repository fact requires repository-relative file evidence and a symbol when one can be identified. Evidence describes current snapshot state; implementation targets describe future state and may use `create` for paths that do not exist.

Do not infer another provider's work. Make every implementation step concrete and include targets, changes, reason, and tests.

