# Synthesis phase

Synthesize the validated anonymous proposals and reviews into one final structured plan. Do not vote by majority and do not infer authorship. Resolve conflicts in this priority order: explicit project invariants, verified repository evidence, existing tests, existing architecture, correctness, backward compatibility, simplicity, maintainability, implementation cost.

Use the proposals, reviews, and deterministic validations. You may use read-only
repository tools to resolve a concrete disagreement or evidence gap, beginning
at the suggested paths and expanding only when necessary. Do not modify anything.
Prefer the smallest complete plan and omit repetitive rationale.

Every `supportedBy` value must be the ID of a claim or architectural decision
declared in the same artifact. Decision-to-decision dependencies are allowed;
undeclared support IDs are not.

Conclave assigns every unresolved review disagreement a stable ID in
`.conclave-input/disagreement-catalog.json`. Preserve every catalog ID exactly
once in `councilDisagreements[].sourceIds`. You may group semantically related
IDs in one entry and paraphrase them in `summary`; exact text copying is neither
required nor desired. Do not invent IDs. Keep genuinely unresolved matters
visible in the summary and, when action still requires a user decision, also in
`openQuestions`.

Never present invalid evidence as truth. Produce `final-plan.json` data only and
satisfy `output-schema.json`; Markdown will be rendered by deterministic code.
Descriptive collections may be empty when not applicable; preserve the required
goal, concrete implementation steps, tests, and all disagreement catalog IDs.
