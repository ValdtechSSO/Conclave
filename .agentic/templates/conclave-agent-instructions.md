## Conclave planning

- Use Conclave before implementing a non-trivial feature, migration, or
  architectural change, unless the user requests implementation without a
  council plan.
- Run `conclave doctor` first and resolve failed readiness checks without
  exposing credentials.
- Inspect only enough repository structure to select the best starting paths.
  Prefer the owning feature/component directory. The provider may follow direct
  dependencies, consumers, contracts, and tests elsewhere when evidence requires
  it, so the caller does not need to enumerate the complete dependency graph.
- Never use `--whole-repository` without explicit user authorization.
- Pin every selected provider model via verified configuration or `--models`;
  never accept an implicit provider default.
- Prefer two providers that satisfy quorum. Add another provider only when its
  additional cost is justified.
- Invoke Conclave with live machine-readable progress, for example:
  `conclave plan --id "<unique-id>" --directory "$PWD" --prompt-file "<request-file>" --scope "<path,path>" --providers "<provider,provider>" --models "<provider=model,provider=model>" --snapshot working-tree --max-cost-usd 0.50 --progress-format jsonl --json`.
- Relay meaningful provider/phase and `activityCode` changes from stderr to the
  user. These are observable activities, not private reasoning. If the IDE loses
  the stream, read it with `conclave show <run-id> --progress`.
- Read and implement the validated plan at the returned `planPath`, unless the
  user requested planning only.
- Never start another paid run automatically after a timeout, crash, billing
  failure, invalid artifact, or budget failure. Report the retained diagnostics
  and request approval first.
- Never use Claude for a live-provider test. An explicitly authorized smoke test
  must select only `codex=gpt-5.6-terra` and
  `deepseek=deepseek-v4-flash`. Automated tests must use fake providers and make
  no paid API calls.
- Do not use `--development`, disable progress, or relax evidence gates unless
  the user explicitly requests that tradeoff.
