# System overview

Conclave is a local .NET CLI with stable commands (`plan`, `show`, `doctor`, `prune`). The CLI delegates application behavior to feature slices in orchestration. Core contains contracts only; adapters isolate provider-specific behavior; repository services own Git snapshots and worktrees; validation owns deterministic gates; infrastructure owns processes, persistence, configuration, budgets, and retention.

The project split follows the implementation plan. Inside each project, new behavior remains feature-first. Dependencies point inward toward `Conclave.Core`; provider, Git, filesystem, and process details do not enter Core.

