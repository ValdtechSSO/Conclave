# Global invariants

1. Every phase evaluates the same retained snapshot SHA.
2. Providers never execute in the original working tree.
3. Original logical repository state must be identical before and after a run.
4. Internal artifacts are schema-constrained JSON.
5. Repository facts require admissible evidence.
6. Providers never review their own proposals.
7. Authorship and presentation order are neutralized.
8. Majority agreement is not evidence.
9. Resource budgets are checked before every provider call; token consumption is isolated per provider and is never aggregated across providers.
10. Markdown is a deterministic render of a validated final plan.
11. Provider prompts contain recommended search roots, not preloaded repository contents; providers inspect the retained snapshot with read-only tools.
12. Provider model selection is explicit and auditable; implicit provider defaults are prohibited.
13. Every paid attempt is recorded, including timeouts, failures, retries, usage, and reported cost.
14. Recommended roots are not an evidence boundary. Providers may expand only to follow a direct dependency, consumer, contract, test, or concrete evidence gap; speculative crawling is prohibited.
15. Live activity contains only assigned tasks and observable public events; private reasoning and generated content never enter the progress stream.
16. Repository files and phase artifacts are read from the isolated worktree and are never bulk-embedded in provider prompts.
17. A provider phase never invokes Conclave recursively, another provider CLI, or a delegated agent.
18. Every unresolved review disagreement receives a stable anonymous catalog ID, and a final plan must reference every catalog ID exactly once; human-readable summaries may paraphrase or group related entries.
19. Any local structured-output repair is deterministic, narrowly scoped, explicitly audited, and cannot bypass the authoritative schema or downstream validation.
20. Evidence symbol normalization is limited to deterministic source-language constructs; runtime interpolation and fuzzy or approximate matching are prohibited.
21. A recoverable representation defect must not discard an otherwise usable provider artifact: deterministic normalization is preferred, is surfaced as a warning, and never invents repository facts or implementation content.
22. Validation remains blocking for unsafe paths, wholly absent evidence files, ambiguous artifact identity, conflicting implementation targets, missing executable plan content, and dropped cataloged disagreements.
