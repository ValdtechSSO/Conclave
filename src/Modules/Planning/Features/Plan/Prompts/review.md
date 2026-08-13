# Cross-review phase

Review the anonymous proposals and deterministic validations, and independently
check their claims with read-only repository tools. Begin at the suggested paths,
but follow direct dependencies, consumers, contracts, tests, or evidence gaps
when necessary. Keep exploration focused, do not modify anything, and do not
infer authorship. Agreement between proposals is not evidence.

Identify only material incorrect assumptions, architectural violations, missing
invariants, unnecessary complexity, migration and compatibility risks, concurrency
and security concerns, missing tests, rollout risks, strongest ideas, and unresolved
disagreements. Repository facts introduced by the review require evidence. Keep
the review concise. In `proposalAliases`, use each bare anonymous alias from the
input filename: for example, `proposal-Y7.json` must be reported as `Y7` (without
`proposal-` or `.json`). Produce JSON only and satisfy `output-schema.json`.

Concern collections may be empty when no material issue exists; do not invent a
finding merely to populate a category.
