# Conclave — Implementation Plan

> **Status:** Revised design  
> **Version:** V1 implementation plan  
> **Primary goal:** Produce implementation plans through independent multi-model analysis of the same immutable repository snapshot, with structured artifacts and deterministic validation.

> **Product name:** `Conclave`. The earlier working name “Architecture Council” is retired from the implementation surface; commands, namespaces, paths, refs, configuration, and artifacts use `Conclave` / `conclave`.

---

## 1. Objective

Build an independent CLI tool called `conclave` that can be invoked by any coding agent such as Codex, Claude, or another local agent.

Example:

```bash
conclave plan \
  --id EXPENSE-CATEGORIES-001 \
  --directory /Users/adrian/Software_projects/Vyntrio \
  --prompt-file /tmp/feature.md \
  --json
```

Conclave must:

1. receive the target repository;
2. receive the feature description, requirements, and constraints;
3. create one immutable repository snapshot for the whole run;
4. create isolated disposable workspaces for the participating providers;
5. allow each provider to inspect, build, test, and use scratch files inside its own workspace;
6. generate independent structured proposals;
7. validate repository evidence deterministically;
8. perform cross-review without self-review;
9. synthesize disagreements without majority voting;
10. validate the final structured plan;
11. render `implementation-plan.md`;
12. return a machine-readable result to the calling agent.

The original working repository must never be modified by the planning run.

---

# 2. Core architectural principle

The calling coding agent must know only Conclave's public contract.

```text
Codex / Claude / another agent
            │
            │ conclave plan
            ▼
      ┌─────────────┐
      │  Conclave   │
      └──────┬──────┘
             │
             ▼
     Immutable Snapshot
             │
     ┌───────┼────────┐
     ▼       ▼        ▼
 Claude    Codex   DeepSeek
workspace workspace workspace
     │       │        │
     └───────┼────────┘
             ▼
      Structured artifacts
             │
             ▼
   Deterministic validation
             │
             ▼
   implementation-plan.md
             │
             ▼
        Calling agent
             │
             ▼
         Human review
```

The external CLI contract must remain stable even if providers, prompts, schemas, orchestration, or execution strategies change later.


## V1 invariants

These invariants are part of the architecture contract, not prompt conventions.

```text
INV-01
Every phase of a run evaluates the same immutable SnapshotSha.

INV-02
SnapshotSha remains reachable for the full retention lifetime
of the Conclave run.

INV-03
Providers never execute against the user's original working tree.

INV-04
The logical state of the original repository must be identical
before and after Conclave execution.

INV-05
Provider workspaces are disposable and reset between stages.

INV-06
Intermediate phase contracts are schema-constrained data,
not prose.

INV-07
Repository facts require deterministically checked evidence.

INV-08
Architectural reasoning is not treated as a repository fact.

INV-09
Evidence paths describe observed state and must exist in SnapshotSha.
Implementation targets describe future state and need not exist.

INV-10
A provider never reviews its own proposal.

INV-11
Proposal authorship is hidden and aliases/order are randomized.

INV-12
Majority agreement is never considered evidence.

INV-13
Synthesis has an explicit fallback chain.

INV-14
Provider/model selection may vary by Conclave stage.

INV-15
Every run and provider executes within explicit resource budgets.

INV-16
Provider failures are classified before quorum evaluation.

INV-17
Workspaces are deleted by default after the run.

INV-18
Run artifacts and snapshot refs share one retention lifecycle.

INV-19
Markdown is only a deterministic render of validated FinalPlan data.

INV-20
No repository fact may be presented as established truth without
admissible validated evidence.
```

---

# 3. V1 scope

Implement:

```text
conclave plan
conclave show
conclave doctor
conclave prune
```

V1 uses locally installed provider CLIs.

Initially supported adapters:

```text
Claude CLI
Codex CLI
DeepSeek CLI
```

V1 also includes operational hardening required for safe daily use:

- immutable snapshot refs with a retention lifecycle;
- final original-repository integrity assertion;
- run-level and provider-level resource budgets;
- workspace cleanup by default;
- run retention and pruning;
- provider/model configuration per Conclave stage.

Do not implement yet:

- MCP;
- Web UI;
- autonomous implementation;
- PR review;
- persistent agent conversations;
- vector databases;
- embeddings;
- complex context assembly;
- dynamic provider benchmarking;
- remote distributed execution;
- automatic architectural approval.

Future capabilities must be able to reuse the same core pipeline.

---

# 4. Suggested installation model

Conclave should be an independent project:

```text
~/ai-tools/
└── conclave/
```

It should expose a global executable:

```bash
conclave
```

Target repositories do not contain Conclave itself.

They may optionally contain project-specific configuration:

```text
Vyntrio/
├── AGENTS.md
├── CLAUDE.md
├── src/
├── tests/
├── docs/
└── .conclave.yaml
```

This allows Conclave to evolve independently from the applications that use it.

---

# 5. Project structure

```text
conclave/
│
├── src/
│   ├── Modules/
│   │   └── Planning/
│   │       ├── AGENTS.md
│   │       ├── module.contract.yml
│   │       ├── Contracts/
│   │       ├── Features/
│   │       │   ├── Plan/
│   │       │   │   ├── Prompts/
│   │       │   │   ├── Schemas/
│   │       │   │   └── Validation/
│   │       │   ├── Environment/
│   │       │   └── Run/
│   │       │       ├── ShowRunService.cs
│   │       │       └── PruneRunService.cs
│   │       └── Infrastructure/
│   │           ├── Configuration/
│   │           ├── Git/
│   │           ├── Persistence/
│   │           ├── Processes/
│   │           └── Providers/
│   └── Hosts/
│       └── Cli/
│
├── tests/
│   ├── Unit/Planning/
│   ├── Integration/Planning/
│   └── EndToEnd/Cli/
│
├── AGENTS.md
├── README.md
└── Conclave.sln
```

## Responsibilities

Only directories backed by current code or an enforced boundary are created.
The reference architecture is not materialized as a set of empty placeholders.

### Planning module

`src/Modules/Planning` owns planning-run vocabulary, contracts, behavior, and
technical implementations. Application behavior starts in one of its explicit
feature areas:

- `Plan` coordinates snapshot, proposal, validation, cross-review,
  synthesis, final validation, and rendering;
- `Environment` checks provider and repository prerequisites;
- `Run` owns inspection, progress access, retention, and snapshot cleanup for
  existing planning runs.

The module uses two assemblies. `Conclave.Planning` contains contracts and
features and does not reference infrastructure. `Conclave.Planning.Infrastructure`
implements provider, Git, process, persistence, and configuration ports.

Technical categories do not become modules, and another assembly is added only
when it enforces a current dependency, deployment, ownership, language, or
runtime boundary.

### CLI host

Responsible for:

```text
plan
show
doctor
prune
```

and console argument parsing, module composition, progress presentation, and
human/machine-readable output. It does not own application behavior.

---

# 6. Public CLI contract

Primary command:

```bash
conclave plan \
  --id <run-id> \
  --directory <repository> \
  --prompt-file <feature-file> \
  --json
```

Alternative inline prompt:

```bash
conclave plan \
  --id EXP-001 \
  --directory . \
  --prompt "Add hierarchical expense categories" \
  --json
```

Recommended future-safe options:

```text
--output <path>
--providers <provider-list>
--snapshot head|working-tree
--evidence-policy fail|annotate
--json
```

The caller must not need to know:

- which provider performs synthesis;
- how workspaces are created;
- where intermediate artifacts live;
- how prompts are composed;
- how evidence is validated.

---

# 7. Domain contracts

## ConclaveRequest

```csharp
public sealed record ConclaveRequest(
    string RunId,
    string RepositoryPath,
    string FeaturePrompt,
    SnapshotMode SnapshotMode,
    string? OutputPath);
```

```csharp
public enum SnapshotMode
{
    Head,
    WorkingTree
}
```

## ModelRequest

```csharp
public sealed record ModelRequest(
    string RunId,
    ConclaveStage Stage,
    string Prompt,
    string WorkingDirectory,
    string OutputSchemaPath);
```

## ConclaveStage

```csharp
public enum ConclaveStage
{
    Proposal,
    Review,
    Synthesis
}
```

## Provider failure classification

```csharp
public enum ProviderFailureKind
{
    None,
    Timeout,
    RateLimit,
    Authentication,
    ProcessCrash,
    InvalidStructuredOutput,
    ContextLimit,
    Cancelled,
    Unknown
}
```

## Usage metrics

```csharp
public sealed record UsageMetrics(
    long? InputTokens,
    long? CachedInputTokens,
    long? OutputTokens,
    decimal? Cost,
    string? Currency);
```

## Participant identity

Provider identity and model identity are separate concepts.

```csharp
public sealed record ParticipantIdentity(
    string ProviderId,
    string ModelId);
```

A stage invocation combines:

```text
ParticipantIdentity
+
ConclaveStage
```

This allows one provider to use different models for proposal, review, and synthesis without changing the provider adapter.

Review self-exclusion remains provider-level:

```text
A provider does not review a proposal produced by that same provider.
```

Synthesis conflict avoidance is participant-level:

```text
Prefer a provider/model pair that did not author a proposal.
```

## Model execution result

```csharp
public sealed record ModelExecutionResult(
    ParticipantIdentity Participant,
    ConclaveStage Stage,
    bool Success,
    ProviderFailureKind FailureKind,
    string? Content,
    UsageMetrics Usage,
    TimeSpan Duration,
    int? ExitCode,
    string? Error);
```

## Provider adapter

```csharp
public interface IModelAdapter
{
    string Id { get; }

    Task<ModelExecutionResult> ExecuteAsync(
        ModelRequest request,
        CancellationToken cancellationToken);
}
```

The orchestrator must never contain provider conditionals such as:

```csharp
if (provider == "claude")
```

Provider-specific behavior belongs inside adapters.

---

# 8. Configuration

Provider execution is configurable independently by stage.

Example:

```yaml
providers:

  claude:
    enabled: true
    command: claude
    promptTransport: stdin
    timeoutSeconds: 900

    proposal:
      model: claude-proposal-model

    review:
      model: claude-review-model

    synthesis:
      model: claude-arbiter-model

  codex:
    enabled: true
    command: codex
    promptTransport: stdin
    timeoutSeconds: 900

    proposal:
      model: codex-proposal-model

    review:
      model: codex-review-model

    synthesis:
      model: codex-arbiter-model

  deepseek:
    enabled: true
    command: deepseek
    promptTransport: stdin
    timeoutSeconds: 900

    proposal:
      model: deepseek-proposal-model

    review:
      model: deepseek-review-model

    synthesis:
      model: deepseek-arbiter-model

conclave:
  minimumProposalQuorum: 2
  minimumReviewQuorum: 2

synthesis:
  fallback:
    - provider: codex
      model: codex-arbiter-model
    - provider: claude
      model: claude-arbiter-model
    - provider: deepseek
      model: deepseek-arbiter-model

evidence:
  unverifiablePolicy: annotate

budget:
  run:
    maxTokens: 2000000
    maxDurationMinutes: 30

  provider:
    maxTokens: 800000
    maxDurationMinutes: 20
    maxCalls: 4

  abortOnExceeded: true

retention:
  keepRuns: 20
  maxAgeDays: 30
  keepWorkspaces: false

retry:
  rateLimitAttempts: 3
  timeoutAttempts: 1
  invalidStructuredOutputAttempts: 1
```

Provider-specific budget overrides may be supported:

```yaml
providers:
  claude:
    budget:
      maxTokens: 900000
      maxDurationMinutes: 20
      maxCalls: 4
```

Configuration precedence:

```text
built-in defaults
        ↓
~/.conclave/config.yaml
        ↓
<repository>/.conclave.yaml
        ↓
CLI arguments
```

The configuration model must distinguish:

```text
provider
model
stage
```

rather than assuming one model per provider.

---

# 9. Immutable repository snapshot

This replaces the original live-repository fingerprint as the execution mechanism.

The entire Conclave run must operate against one immutable `SnapshotSha`.

```text
repository
    │
    ▼
Snapshot Service
    │
    ▼
SnapshotSha
    │
    ├─────────────┬─────────────┐
    ▼             ▼             ▼
workspace A   workspace B   workspace C
```

All proposals, reviews, and synthesis must see exactly the same repository state.

## Clean repository

For:

```text
--snapshot head
```

use the current:

```text
HEAD SHA
```

as `SnapshotSha`.

## Dirty repository

For:

```text
--snapshot working-tree
```

Conclave must create an ephemeral snapshot commit representing:

- tracked files;
- staged changes;
- unstaged changes;
- untracked non-ignored files.

It must not:

- move the user's branch;
- modify the user's working tree;
- modify the user's real index.

A robust implementation can use a temporary Git index:

```text
GIT_INDEX_FILE=<temporary-index>
```

Conceptually:

```bash
git read-tree HEAD
git add -A
git write-tree
git commit-tree <tree>
```

executed against the temporary index.

The resulting commit becomes:

```text
SnapshotSha
```

Ignored files are excluded by default.

## Persist the snapshot with a real ref

A `commit-tree` commit without a ref becomes unreachable after the disposable worktrees are removed and may later be deleted by Git garbage collection.

Every run must therefore pin its snapshot with a real Conclave-owned ref:

```bash
git update-ref \
  refs/conclave/runs/<run-key> \
  <SnapshotSha>
```

Use an internal sanitized `run-key` or generated ULID/UUID rather than interpolating an arbitrary user-supplied `--id` directly into a ref name.

The lifecycle is:

```text
Create SnapshotSha
        ↓
Create refs/conclave/runs/<run-key>
        ↓
Create provider workspaces
        ↓
Execute Conclave
        ↓
Destroy workspaces by default
        ↓
KEEP snapshot ref
        ↓
Retention expires
        ↓
conclave prune
        ↓
Delete run artifacts + snapshot ref
```

The snapshot ref is part of the run's audit record.

## Snapshot metadata

Persist:

```json
{
  "runKey": "01J...",
  "baseHead": "abc123",
  "snapshotSha": "def456",
  "snapshotRef": "refs/conclave/runs/01J...",
  "snapshotMode": "working-tree",
  "includedWorkingTreeChanges": true,
  "includedUntrackedFiles": true,
  "includedIgnoredFiles": false
}
```

The central invariant is:

> Every Conclave participant and every Conclave phase evaluates exactly the same source tree, and that source tree remains reachable for the full retention lifetime of the run.

## Final original-repository integrity assertion

Worktrees are the primary isolation mechanism, but Conclave must keep one final inexpensive safety assertion against catastrophic adapter/orchestrator mistakes.

Capture the logical state of the original repository before Conclave performs any run operation:

```text
HEAD OID
IndexTree OID
TrackedWorkingTreeDiffHash
UntrackedContentHash
```

Recommended logical representation:

```text
HEAD OID
    git rev-parse HEAD

IndexTree OID
    git write-tree

TrackedWorkingTreeDiffHash
    hash(git diff --binary HEAD)

UntrackedContentHash
    hash(sorted non-ignored untracked paths + file contents)
```

Do not hash the physical `.git/index` file. Git may refresh internal index metadata without changing its logical staged content.

After:

```text
Conclave execution
+
workspace cleanup
```

capture the same logical state again.

Comparison:

```text
Before
   vs
After
```

If any logical component differs:

```text
12 ORIGINAL_REPOSITORY_MUTATED
```

Conclave must fail loudly and report the changed state.

It must never silently revert the user's repository.

This assertion is a last-resort integrity guard; it is not a substitute for provider workspace isolation.

---

# 10. Provider workspaces

Create one disposable workspace per provider from `SnapshotSha`.

Example:

```text
~/.conclave/runs/EXP-001/
│
├── workspaces/
│   ├── claude/
│   ├── codex/
│   └── deepseek/
│
├── proposals/
├── reviews/
├── synthesis/
└── result.json
```

Recommended V1 mechanism:

```bash
git worktree add --detach <workspace> <SnapshotSha>
```

Each provider runs only inside its own workspace.

The provider may:

- inspect source;
- search source;
- build;
- run tests;
- create scratch files;
- generate temporary code;
- inspect `git diff`.

The provider may modify its disposable workspace.

The user's original working tree is never the provider working directory.

## Important Git boundary

Linked worktrees share parts of the parent Git repository metadata.

Therefore V1 must not claim that a linked worktree is a perfect security boundary.

Mitigations:

1. run provider tools with the safest available sandbox/restrictions;
2. disable unnecessary network access where supported;
3. prohibit push/remote mutation through adapter policy;
4. record shared Git refs/config before and after provider execution where useful;
5. treat unexpected shared-metadata mutations as workspace failure;
6. always execute the final original-repository integrity assertion from §9.

A future stronger-isolation mode may use disposable local clones instead of linked worktrees.

---

# 11. Workspace lifecycle

A provider workspace may be reused across stages, but it must be reset before every stage.

Before Proposal / Review / Synthesis:

```bash
git reset --hard <SnapshotSha>
git clean -fdx
```

Then Conclave injects only the inputs required for that stage.

This prevents:

- build outputs from leaking between stages;
- scratch code from influencing later reasoning;
- proposal-stage modifications from affecting review;
- one phase accidentally reading stale Conclave inputs.

---

# 12. Conclave-owned workspace inputs

Provider sandboxes may not be able to read:

```text
~/.conclave/runs/...
```

Therefore stage inputs must be materialized inside the provider workspace.

Use:

```text
.conclave-input/
```

Example during review:

```text
workspace/
├── src/
├── tests/
├── AGENTS.md
├── CLAUDE.md
└── .conclave-input/
    ├── CONCLAVE.md
    ├── feature.md
    ├── proposal-x.json
    ├── proposal-y.json
    └── output-schema.json
```

`.conclave-input/`:

- is generated by Conclave;
- is ephemeral;
- is never part of the repository snapshot;
- is removed during workspace reset;
- must be ignored when calculating repository evidence.

Canonical intermediate artifacts remain under the Conclave run directory.

---

# 13. Shared Conclave brief

Different CLIs may automatically consume different repository guidance such as:

```text
AGENTS.md
CLAUDE.md
provider-native configuration
```

Conclave should not pretend those differences do not exist.

Instead it must provide one common high-priority briefing to every provider:

```text
.conclave-input/CONCLAVE.md
```

The shared brief contains:

```text
run ID
snapshot SHA
feature
requirements
constraints
phase
Conclave rules
output contract
evidence rules
```

Each adapter is responsible for ensuring the Conclave brief is included in the provider invocation.

Provider-native repository guidance may still be loaded because it represents real repository context.

For traceability, metadata should record which known instruction files were present for each provider.

The invariant is:

> Conclave-specific instructions are identical across providers, even when provider-native repository context differs.

---

# 14. Structured contracts between phases

Markdown is not an internal Conclave protocol.

Internal artifacts must be structured JSON validated against Conclave-owned schemas.

Canonical artifacts:

```text
proposal-a.json
proposal-b.json
proposal-c.json

review-a.json
review-b.json
review-c.json

final-plan.json
```

Markdown is generated only after final validation.

---

# 15. Proposal schema

A proposal should contain concepts such as:

```json
{
  "summary": "Introduce immutable category keys and explicit hierarchy metadata.",

  "claims": [
    {
      "id": "CLAIM-001",
      "kind": "repository_fact",
      "statement": "Historical aggregation uses Category.Key prefix matching.",
      "evidence": [
        {
          "file": "src/.../ExpenseAggregationService.cs",
          "symbol": "AggregateAsync",
          "kind": "source"
        }
      ]
    }
  ],

  "decisions": [
    {
      "id": "DEC-001",
      "statement": "Category keys must remain immutable.",
      "supportedBy": [
        "CLAIM-001"
      ]
    }
  ],

  "implementationSteps": [
    {
      "id": "STEP-001",
      "targets": [
        {
          "path": "src/NewCategoryService.cs",
          "operation": "create"
        },
        {
          "path": "src/ExistingService.cs",
          "operation": "modify"
        }
      ],
      "changes": "Add hierarchical category resolution while preserving immutable keys.",
      "reason": "Preserve historical aggregation semantics.",
      "tests": [
        "Add regression coverage for historical roll-up behavior."
      ]
    }
  ],

  "risks": [],
  "alternatives": [],
  "openQuestions": []
}
```

Recommended target operations:

```text
create
modify
delete
rename
move
generated
```

The real JSON Schema is the contract.

Prompts explain the semantics but cannot replace schema validation.

## Evidence paths and implementation targets are different concepts

This distinction is an invariant:

```text
Evidence path
-------------
Describes observed repository state.
MUST resolve against SnapshotSha.

Implementation target
---------------------
Describes intended future state.
MUST NOT be evidence-gated by existence in SnapshotSha.
```

Conceptually:

```text
evidence = IS

target = WILL BE
```

A valid implementation plan may create a file that does not exist in the snapshot.

---

# 16. Claim types

Not every architectural statement can be deterministically proven.

Conclave must distinguish at least:

```text
repository_fact
architectural_reasoning
assumption
external_constraint
```

## repository_fact

Must provide repository evidence.

Example:

```text
"This query aggregates descendants by key prefix."
```

## architectural_reasoning

Does not require mechanical proof.

Example:

```text
"This alternative increases coupling between persistence and domain semantics."
```

## assumption

Must be explicitly surfaced and may become an open question.

## external_constraint

Represents requirements supplied by the user or calling agent rather than discovered in the repository.

This avoids incorrectly treating architectural judgment as a failed repository lookup.

---

# 17. Proposal phase

All providers generate proposals independently and concurrently.

```text
             Feature
                │
      ┌─────────┼─────────┐
      ▼         ▼         ▼
   Claude     Codex    DeepSeek
      │         │         │
      ▼         ▼         ▼
proposal A proposal B proposal C
```

No provider receives another proposal during this phase.

Example orchestration:

```csharp
await Task.WhenAll(
    claude.ExecuteAsync(...),
    codex.ExecuteAsync(...),
    deepseek.ExecuteAsync(...)
);
```

Each provider receives:

```text
repository workspace
shared Conclave brief
feature
proposal schema
```

---

# 18. Proposal prompt responsibilities

The proposal prompt must instruct the provider to:

- inspect the repository itself;
- inspect relevant source;
- inspect relevant tests;
- inspect ADRs/documentation;
- inspect Git history when useful;
- build or run targeted tests when useful;
- avoid assuming the feature description contains all relevant context;
- distinguish repository facts from architectural reasoning;
- attach evidence to repository facts;
- produce output matching the proposal schema.

The provider is allowed to modify only its disposable workspace.

---

# 19. Deterministic evidence validation

This is a mandatory V1 component.

LLMs propose evidence.

Conclave decides whether that evidence is admissible.

```text
LLM output
    │
    ▼
claim + evidence
    │
    ▼
EvidenceValidator
    │
    ├── verified
    ├── unverifiable
    └── invalid
```

Interface:

```csharp
public interface IEvidenceValidator
{
    Task<EvidenceValidationResult> ValidateAsync(
        ConclaveArtifact artifact,
        RepositorySnapshot snapshot,
        CancellationToken cancellationToken);
}
```

---

# 20. Evidence validation rules

For every `repository_fact`:

### File validation

Verify that:

```text
evidence.file
```

exists in `SnapshotSha`.

### Symbol validation

Verify that the referenced symbol appears in the referenced evidence file.

V1 may start with conservative text/symbol matching behind an abstraction:

```csharp
public interface ISymbolEvidenceResolver
```

Future implementations can provide:

- Roslyn resolution for C#;
- TypeScript AST resolution;
- language-server based resolution;
- tree-sitter based resolution.

### Evidence linkage

Verify that:

- every repository fact has at least one evidence entry;
- every `supportedBy` claim ID exists;
- decisions do not reference nonexistent claims.

### Implementation-target validation

Implementation targets are **not evidence**.

For:

```json
{
  "path": "src/NewService.cs",
  "operation": "create"
}
```

Conclave must validate structural properties such as:

- path is non-empty;
- path is repository-relative unless explicitly allowed otherwise;
- operation is a supported value;
- duplicate/conflicting target operations are surfaced.

Conclave must **not** require `target.path` to exist in `SnapshotSha`.

For operations that semantically require an existing object, such as:

```text
modify
delete
rename source
move source
```

Conclave may emit a structural warning or validation rule if the target cannot be resolved, but this is separate from repository-evidence validation.

A new-file target with:

```text
operation = create
```

must never cause:

```text
PLAN_EVIDENCE_UNVERIFIABLE
```

### Implementation-step completeness

Every step must contain non-empty:

```text
Targets
Changes
Reason
Tests
```

unless the schema explicitly marks a field as not applicable.

---

# 21. Evidence statuses

Use at least:

```text
Verified
Unverified
Invalid
NotDeterministicallyVerifiable
```

Examples:

### Verified

File and symbol exist in the snapshot.

### Unverified

Evidence exists syntactically but Conclave cannot prove it with the available resolver.

### Invalid

The claimed file or symbol does not exist.

### NotDeterministicallyVerifiable

Used only where mechanical validation is conceptually inappropriate.

Do not classify architectural reasoning as hallucination simply because it cannot be resolved like a symbol.

---

# 22. Evidence gate

Pipeline:

```text
Proposal JSON
     │
     ▼
JSON Schema Validation
     │
     ▼
Evidence Validation
     │
     ▼
Eligible Proposal
```

The validation result must be persisted.

Example:

```json
{
  "totalRepositoryClaims": 17,
  "verified": 15,
  "unverified": 1,
  "invalid": 1,
  "evidenceScore": 0.882
}
```

The score is diagnostic.

It must not become a hidden majority-voting mechanism.

Configuration:

```yaml
evidence:
  unverifiablePolicy: annotate
```

Supported policies:

```text
annotate
fail
```

Recommended:

```text
local development → annotate
strict/CI          → fail
```

Invalid evidence should always be clearly surfaced to later phases.

---

# 23. Proposal anonymization

Provider identity must not appear in review or synthesis inputs.

Do not use a stable mapping such as:

```text
Claude   → Proposal A
Codex    → Proposal B
DeepSeek → Proposal C
```

Instead generate opaque aliases randomly for every run.

Example:

```text
Claude   → Proposal K7
Codex    → Proposal M2
DeepSeek → Proposal Q9
```

The mapping changes from run to run.

Conclave persists the real mapping only in private run metadata for:

- diagnostics;
- usage accounting;
- auditability.

It must never inject that mapping into reviewer or synthesizer inputs.

## Randomize presentation order

Proposal order must also be randomized independently for each reviewer.

Example:

```text
Reviewer 1:
Q9
K7

Reviewer 2:
M2
Q9

Reviewer 3:
K7
M2
```

Synthesis input order should also be shuffled.

This removes both:

```text
provider → stable alias bias
```

and:

```text
first proposal → stable positional bias
```

Randomization is a presentation-neutrality mechanism, not a scoring mechanism.

---

# 24. Cross-review without self-review

A provider reviews only proposals authored by other providers.

With three providers:

```text
Claude    reviews B + C
Codex     reviews A + C
DeepSeek  reviews A + B
```

Do not perform:

```text
Claude reviews A + B + C
```

Benefits:

- avoids self-review bias;
- reduces token consumption;
- reduces duplicated commentary;
- focuses each reviewer on alternatives.

---

# 25. Review contract

Reviews must also be JSON-schema constrained.

A review should identify:

```text
incorrect assumptions
architectural violations
missing invariants
unnecessary complexity
migration risks
backward compatibility problems
concurrency problems
security concerns
missing tests
rollout risks
strongest ideas
unresolved disagreements
```

Reviews may introduce repository-fact claims.

Those claims are subject to the same EvidenceValidator.

---

# 26. Review inputs

Before each review:

1. reset the reviewer's workspace to `SnapshotSha`;
2. clean all build/scratch artifacts;
3. inject `.conclave-input/`;
4. include only the two foreign proposals;
5. include proposal evidence-validation results;
6. include review schema;
7. execute the provider.

Reviewers may independently inspect, build, and test against the repository snapshot.

They must not trust a proposal merely because multiple proposals agree.

---

# 27. Review evidence gate

Review pipeline:

```text
Review JSON
    │
    ▼
Schema Validation
    │
    ▼
Evidence Validation
    │
    ▼
Eligible Review
```

Invalid review evidence must be annotated before synthesis.

---

# 28. Synthesis strategy

Synthesis receives:

```text
feature
validated proposals
validated reviews
repository snapshot
```

It must produce:

```text
final-plan.json
```

not Markdown.

---

# 29. Synthesizer selection and fallback

Do not hardcode one provider/model as the permanent synthesizer.

Provider and model are configurable by stage.

Example:

```yaml
providers:
  codex:
    proposal:
      model: codex-proposal-model

    review:
      model: codex-review-model

    synthesis:
      model: codex-arbiter-model
```

This makes:

```text
Codex / proposal-model
```

and:

```text
Codex / arbiter-model
```

different Conclave participants even though they use the same CLI/provider.

Use an ordered synthesis fallback chain:

```yaml
synthesis:
  fallback:
    - provider: codex
      model: codex-arbiter-model
    - provider: claude
      model: claude-arbiter-model
    - provider: deepseek
      model: deepseek-arbiter-model
```

Selection policy:

1. consider only available provider/model participants;
2. prefer a provider/model pair that did not author a proposal;
3. if the provider participated in proposals but the configured synthesis model is different, treat it as a distinct participant for synthesis-bias reduction;
4. hide authorship of all proposals and reviews;
5. randomize synthesis input order;
6. if the selected synthesizer fails with a retryable error, apply retry policy;
7. if it remains unavailable, move to the next eligible fallback participant;
8. fail only when no synthesizer remains.

This gives V1 a practical "fourth arbiter" option without requiring a fourth CLI provider.

A future deployment may still choose a genuinely independent fourth provider/model for stronger arbitration independence.

---

# 30. Synthesis rules

The synthesis prompt must explicitly prohibit majority voting.

Resolution priority:

```text
1. Explicit project invariants
2. Verified repository evidence
3. Existing tests
4. Existing architecture
5. Correctness
6. Backward compatibility
7. Simplicity
8. Maintainability
9. Implementation cost
```

Rule:

> Agreement between multiple models is not evidence by itself.

If repository evidence is insufficient to resolve a disagreement, preserve it as:

```text
Open Question
```

or:

```text
Conclave Disagreement
```

---

# 31. Final Plan schema

The synthesizer produces a structured `FinalPlan`.

Conceptually:

```text
Goal
RelevantArchitecture
Invariants
ArchitecturalDecisions
DomainChanges
PersistenceChanges
ApiChanges
AffectedComponents
ImplementationSteps
Migration
Testing
Observability
Security
Risks
RejectedAlternatives
CouncilDisagreements
OpenQuestions
RepositoryEvidence
```

Every implementation step must include:

```text
files/components
changes
reason
tests
```

Every repository-fact claim must reference admissible evidence.

---

# 32. Final deterministic validation

Before rendering Markdown:

```text
final-plan.json
      │
      ▼
JSON Schema Validator
      │
      ▼
Evidence Validator
      │
      ▼
Plan Completeness Validator
      │
      ▼
Markdown Renderer
```

Validation must check at least:

- schema compliance;
- required sections;
- decision IDs are unique;
- claim IDs are unique;
- evidence references resolve;
- repository-fact claims have evidence;
- implementation steps are complete;
- unresolved disagreements are preserved;
- open questions are not silently converted into assumptions;
- no invalid evidence is presented as verified.

---

# 33. Markdown is a render target

Only after final validation:

```text
FinalPlan.json
      │
      ▼
MarkdownRenderer
      │
      ▼
implementation-plan.md
```

Markdown generation must be deterministic code, not another LLM call.

---

# 34. Required Markdown structure

```markdown
# Implementation Plan

## 1. Goal

## 2. Relevant Existing Architecture

## 3. Invariants

## 4. Architectural Decisions

## 5. Domain Changes

## 6. Data Model / Persistence Changes

## 7. API Changes

## 8. Components Affected

## 9. Detailed Implementation Sequence

### Step 1

Targets/components:
Changes:
Reason:
Tests:

## 10. Migration / Backward Compatibility

## 11. Testing Strategy

## 12. Observability

## 13. Security Considerations

## 14. Risks

## 15. Alternatives Rejected

## 16. Conclave Disagreements

## 17. Open Questions

## 18. Repository Evidence

## 19. Conclave Execution Metadata
```

The execution metadata section may include:

```text
snapshot SHA
participating providers
missing providers
evidence warnings
total token usage
duration
```

It should not expose secrets or internal credentials.

---

# 35. Run directory and retention lifecycle

Canonical execution artifacts live outside the repository.

Example:

```text
~/.conclave/runs/
└── EXPENSE-CATEGORIES-001/
    │
    ├── request/
    │   ├── feature.md
    │   └── metadata.json
    │
    ├── private/
    │   └── proposal-author-map.json
    │
    ├── workspaces/
    │   ├── claude/
    │   ├── codex/
    │   └── deepseek/
    │
    ├── proposals/
    │   ├── proposal-k7.json
    │   ├── proposal-m2.json
    │   └── proposal-q9.json
    │
    ├── validation/
    │   ├── proposal-k7-evidence.json
    │   ├── proposal-m2-evidence.json
    │   └── proposal-q9-evidence.json
    │
    ├── reviews/
    │   ├── review-*.json
    │   └── ...
    │
    ├── synthesis/
    │   ├── final-plan.json
    │   └── implementation-plan.md
    │
    ├── logs/
    └── result.json
```

Intermediate artifacts never need to be committed to the target repository.

## Workspace cleanup

Workspaces are disposable.

Default behavior:

```text
run completes
      ↓
remove provider workspaces
      ↓
keep structured artifacts
      ↓
keep logs
      ↓
keep final plan
      ↓
keep snapshot ref
```

Configuration:

```yaml
retention:
  keepWorkspaces: false
```

For debugging:

```bash
conclave plan ... --keep-workspaces
```

`--keep-workspaces` is opt-in and must be recorded in run metadata.

## Run retention

Example:

```yaml
retention:
  keepRuns: 20
  maxAgeDays: 30
  keepWorkspaces: false
```

Run artifacts and:

```text
refs/conclave/runs/<run-key>
```

share one lifecycle.

A run is not fully pruned until both its stored artifacts and its snapshot ref have been removed.

---

# 36. Publication

Canonical plan:

```text
~/.conclave/runs/<ID>/synthesis/implementation-plan.md
```

Optional publication:

```bash
--output ./docs/plans/EXPENSE-CATEGORIES-001.md
```

Only the final validated plan is copied into the target repository.

No proposal, review, workspace scratch file, or temporary Conclave input is copied automatically.

---

# 37. Machine-readable result

With:

```bash
conclave plan ... --json
```

stdout must return a stable result object.

Example:

```json
{
  "runId": "EXPENSE-CATEGORIES-001",
  "runKey": "01J...",
  "status": "completed",
  "snapshotSha": "def456",
  "snapshotRef": "refs/conclave/runs/01J...",
  "planPath": "/Users/adrian/.conclave/runs/EXPENSE-CATEGORIES-001/synthesis/implementation-plan.md",
  "runPath": "/Users/adrian/.conclave/runs/EXPENSE-CATEGORIES-001",
  "proposalCount": 3,
  "reviewCount": 3,
  "warnings": [],
  "budget": {
    "status": "within_limits",
    "maxTokens": 2000000,
    "maxDurationMinutes": 30
  },
  "usage": {
    "inputTokens": 420000,
    "outputTokens": 32000,
    "cost": null,
    "currency": null
  }
}
```

The calling agent must never need to parse conversational text to locate the plan.

---

# 38. Usage accounting and resource budgets

Usage accounting belongs in V1, but accounting alone is insufficient.

Every provider adapter should extract whatever usage data its CLI exposes.

At minimum:

```text
input tokens
cached input tokens
output tokens
duration
cost if available
provider
model
stage
```

Persist both per-call and aggregated usage.

Example:

```json
{
  "participants": {
    "claude/claude-proposal-model": {
      "inputTokens": 183420,
      "outputTokens": 14220,
      "cost": 4.83
    },
    "codex/codex-proposal-model": {
      "inputTokens": 146800,
      "outputTokens": 11840,
      "cost": null
    }
  },
  "total": {
    "inputTokens": 330220,
    "outputTokens": 26060,
    "cost": 4.83
  }
}
```

`cost` may legitimately be unknown when the provider is accessed through a subscription CLI.

Token accounting is still required.

## Budget configuration

Conclave must enforce explicit run and provider limits.

```yaml
budget:

  run:
    maxTokens: 2000000
    maxDurationMinutes: 30

  provider:
    maxTokens: 800000
    maxDurationMinutes: 20
    maxCalls: 4

  abortOnExceeded: true
```

Provider-specific overrides may be supported.

## Budget manager

Introduce a central budget abstraction:

```csharp
public interface IBudgetManager
{
    BudgetDecision CanStart(ModelRequest request);

    void Record(ModelExecutionResult result);
}
```

Before every model call:

```text
Model call requested
        ↓
BudgetManager
     ↙       ↘
  ALLOW      DENY
    ↓          ↓
 execute   budget exit
```

Budget decisions must consider:

- total run elapsed time;
- provider elapsed time;
- total known token usage;
- provider known token usage;
- provider call count;
- configured hard/soft policy.

## Enforcement limitation

Some CLIs report token usage only after the call completes.

For those providers Conclave cannot guarantee termination at the exact token boundary during a single in-flight call.

In that case hard protection relies on:

```text
maxDuration
maxCalls
provider-native limits when available
```

and token ceilings are enforced between calls.

This capability must be represented explicitly in provider capabilities rather than pretending every CLI offers identical real-time token control.

Budget exits:

```text
13 RUN_BUDGET_EXCEEDED
14 PROVIDER_BUDGET_EXCEEDED
```

The purpose is to prevent one pathological planning run from exhausting a user's subscription or operational quota.

---

# 39. Retry and failure policy

Provider failures are not equivalent.

Classify them before quorum evaluation.

## Rate limit

```text
retry with backoff
```

## Timeout

```text
retry according to timeout policy
```

## Authentication

```text
do not retry automatically
mark provider unavailable
```

## Invalid structured output

```text
one constrained repair/retry
then fail provider stage
```

## Process crash

```text
optional single retry
```

## Context limit

```text
do not blindly retry identical request
surface provider-stage failure
```

Quorum is evaluated only after the provider-specific retry policy has been applied.

---

# 40. Quorum

Example:

```yaml
conclave:
  minimumProposalQuorum: 2
  minimumReviewQuorum: 2
```

Scenario:

```text
Claude    ✓
Codex     ✓
DeepSeek  ✗ authentication failure
```

Proposal phase may continue:

```text
2 / 3 quorum satisfied
```

The run metadata must preserve the provider failure.

A single-provider production run must not be presented as a real Conclave result.

Single-provider execution is allowed only as:

- development mode;
- test mode;
- walking-skeleton implementation phase.

---

# 41. Process execution abstraction

Never scatter raw `Process.Start` calls across adapters.

Use:

```csharp
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        ProcessRequest request,
        CancellationToken cancellationToken);
}
```

It must support:

- asynchronous stdout;
- asynchronous stderr;
- stdin;
- timeout;
- cancellation;
- killing the process tree;
- exit code capture;
- environment overrides;
- working directory;
- output-size safeguards.

It must avoid leaking:

- API keys;
- auth tokens;
- sensitive environment variables.

---

# 42. Prompt transport

Do not place large prompts into shell arguments by default.

Support:

```csharp
public enum PromptTransport
{
    Stdin,
    TemporaryFile,
    Argument
}
```

Preferred:

```text
stdin
```

When provider CLI limitations require file-based input, use a Conclave-owned file inside:

```text
.conclave-input/
```

Provider adapters choose the transport.

---

# 43. Provider structured output

Conclave schemas are authoritative.

Where a provider CLI supports schema-constrained output, the adapter should use that capability.

Where it does not, the adapter must still:

1. request JSON-only output;
2. parse the response;
3. validate it against the Conclave schema;
4. allow one repair retry if configured;
5. classify persistent failure as:

```text
InvalidStructuredOutput
```

Do not weaken Conclave schemas to accommodate one provider.

---

# 44. `conclave doctor`

Implement early.

Example:

```bash
conclave doctor
```

It should verify:

```text
Git available
Conclave home writable
provider executable found
provider authentication usable
structured-output capability detected
workspace creation works
snapshot creation works
configuration valid
```

Example output:

```text
Conclave installation

✓ Git
✓ Conclave home
✓ Snapshot creation
✓ Worktree creation

Providers

✓ claude
✓ codex
✓ deepseek

Configuration

✓ minimum proposal quorum: 2
✓ minimum review quorum: 2
✓ evidence policy: annotate

Conclave ready.
```

---

# 45. `conclave show`

```bash
conclave show EXPENSE-CATEGORIES-001
```

Display:

```text
status
snapshot SHA
providers
stage results
duration
usage
warnings
evidence failures
plan path
```

Optional:

```bash
conclave show EXPENSE-CATEGORIES-001 --plan
```

---

# 46. `conclave prune`

Retention is part of V1.

Command:

```bash
conclave prune
```

It applies the configured retention policy to completed/failed runs.

Default cleanup responsibilities:

```text
expired run directory
+
retained provider workspaces
+
refs/conclave/runs/<run-key>
+
stale worktree administrative metadata
```

Support a dry run:

```bash
conclave prune --dry-run
```

Recommended output:

```text
Runs selected: 4
Artifacts to delete: ...
Snapshot refs to delete: 4
Workspaces to remove: 1
```

`conclave prune` must remove a run's snapshot ref only when that run is actually being removed.

A retained run must retain its snapshot ref so that its historical evidence remains auditable.

---

# 47. Exit codes

Define stable exit codes from V1:

```text
0   SUCCESS
2   INVALID_REQUEST
3   PROVIDER_QUORUM_FAILURE
4   WORKSPACE_FAILURE
5   SYNTHESIS_FAILURE
6   CONFIGURATION_ERROR
7   CANCELLED
8   PLAN_EVIDENCE_UNVERIFIABLE
9   STRUCTURED_OUTPUT_INVALID
10  SNAPSHOT_FAILURE
11  FINAL_PLAN_INVALID
12  ORIGINAL_REPOSITORY_MUTATED
13  RUN_BUDGET_EXCEEDED
14  PROVIDER_BUDGET_EXCEEDED
```

Agents should be able to react programmatically.

`ORIGINAL_REPOSITORY_MUTATED` is reserved for failure of the final logical before/after repository integrity assertion, not for ordinary disposable-workspace modifications.

---

# 48. Logging

Per-run logs:

```text
logs/
├── claude-proposal.log
├── codex-proposal.log
├── deepseek-proposal.log
├── claude-review.log
├── codex-review.log
├── deepseek-review.log
└── synthesis.log
```

Separate:

```text
provider output
```

from:

```text
process diagnostics
```

Do not log secrets.

Redact known sensitive environment values before persistence.

---

# 49. Revised implementation strategy: vertical slices

Do not implement the system horizontally by building every abstraction first.

Build one thin end-to-end path, validate it, then expand.

---

## Slice 1 — Walking skeleton with one provider

Build:

```text
CLI
 ↓
IProcessRunner
 ↓
capture OriginalRepositoryState
 ↓
RepositorySnapshotService
 ↓
pin snapshot ref
 ↓
ProviderWorkspace
 ↓
one adapter
 ↓
structured proposal
 ↓
schema validation
 ↓
simple FinalPlan mapping
 ↓
MarkdownRenderer
 ↓
workspace cleanup
 ↓
verify OriginalRepositoryState unchanged
 ↓
implementation-plan.md
```

Use one provider only in development mode.

Example:

```bash
conclave plan \
  --providers codex \
  --id DEV-001 \
  --directory ./sample-app \
  --prompt "Add soft deletion to Customer"
```

### Done when

One real provider can:

- inspect a pinned snapshot workspace;
- build/test if useful;
- emit schema-valid JSON;
- produce a rendered plan;
- leave the user's repository logically untouched;
- leave a reachable snapshot ref after workspace cleanup.

This validates most of the technical risk before multi-provider orchestration exists.

---

## Slice 2 — Evidence and target validation

Add:

```text
claim types
evidence schema
implementation targets
IEvidenceValidator
file validation
symbol validation
decision/claim linkage
step completeness
```

### Done when

- a deliberately hallucinated evidence file or symbol is detected deterministically;
- a valid `create` implementation target that does not yet exist does not fail evidence validation.

---

## Slice 3 — Multi-provider proposals

Add:

```text
Claude adapter
Codex adapter
DeepSeek adapter
per-stage model configuration
parallel execution
opaque randomized proposal aliases
randomized presentation order
proposal quorum
usage aggregation
```

### Done when

At least two providers can independently produce validated proposals against the same `SnapshotSha` without stable provider-to-alias mapping.

---

## Slice 4 — Cross-review

Add:

```text
review schema
workspace reset
.conclave-input materialization
no self-review
independently shuffled review inputs
parallel review
review evidence validation
review quorum
```

### Done when

Each provider reviews only foreign proposals and the reviews are evidence-validated.

---

## Slice 5 — Synthesis

Add:

```text
provider/model participant identity
synthesis model configuration
synthesizer fallback chain
randomized synthesis input order
synthesis schema
final-plan schema
final deterministic validation
```

### Done when

A validated `final-plan.json` is deterministically rendered into `implementation-plan.md`, and failure of one synthesis participant can fall back to another.

---

## Slice 6 — Reliability and operability

Add:

```text
retry policies
failure classification
rate-limit handling
timeouts
run/provider budgets
doctor
show
prune
stable exit codes
cost/token accounting
retention
default workspace deletion
shared Git metadata guard
cleanup/recovery
```

### Done when

Conclave can:

- degrade safely when one provider is unavailable;
- stop additional work when resource budgets are exhausted;
- clean up workspaces by default;
- retain audit artifacts and snapshot refs together;
- prune expired runs and refs deterministically;
- leave actionable diagnostics.

---

# 50. Fundamental tests

## Snapshot consistency

All provider workspaces must resolve:

```text
HEAD == SnapshotSha
```

for the entire run.

## Snapshot ref retention

After provider workspaces are removed:

```text
refs/conclave/runs/<run-key>
```

must still resolve to `SnapshotSha`.

The snapshot must remain readable for a retained run.

## Snapshot prune

When a run expires and `conclave prune` removes it:

- run artifacts are removed;
- the matching snapshot ref is removed;
- unrelated Conclave refs remain intact.

## Dirty snapshot

Create:

- staged changes;
- unstaged changes;
- untracked files.

Verify all appear identically in every provider workspace.

Verify the user's branch and real index remain unchanged.

## Original repository integrity guard

Capture original state.

Run Conclave.

Verify:

```text
HEAD OID
IndexTree OID
TrackedWorkingTreeDiffHash
UntrackedContentHash
```

remain identical.

Then use a deliberately broken fake adapter/orchestrator that modifies the original working tree.

Conclave must return:

```text
12 ORIGINAL_REPOSITORY_MUTATED
```

and must not silently revert the mutation.

## Provider isolation

Changes made by Provider A must never appear in Provider B's workspace.

## Workspace reset

Proposal-stage scratch files must not survive into Review.

## Default workspace cleanup

After a normal completed run, provider workspaces are removed.

## Keep-workspaces debugging mode

With:

```text
--keep-workspaces
```

workspaces are retained and the decision is recorded in metadata.

## Build/test permission

A fake or real provider must be able to create build output inside its disposable workspace without affecting the original repository.

## Proposal independence

Proposal B's input must not contain Proposal A.

## Structured output

Invalid provider JSON must fail schema validation.

## Repair retry

A provider producing invalid JSON once and valid JSON on repair should recover successfully.

## Evidence — missing file

A claim referencing:

```text
src/DoesNotExist.cs
```

must be invalid.

## Evidence — missing symbol

A real evidence file with a nonexistent symbol must be invalid or unverified according to resolver capabilities.

## Evidence vs implementation target

This must fail:

```text
repository_fact evidence.file = missing file
```

This must not fail merely because the file is absent:

```text
implementation target:
  path = new file
  operation = create
```

## Claim typing

`architectural_reasoning` must not fail simply because it lacks repository evidence.

## Decision linkage

A decision referencing nonexistent claim IDs must fail final validation.

## Step completeness

A step missing `Targets`, `Tests`, or `Reason` must fail schema/final-plan validation.

## No self-review

The review input for Provider A must contain only proposals not authored by Provider A.

## Anonymization

Review and synthesis inputs must not reveal provider authorship.

Alias generation must not be a deterministic provider mapping such as:

```text
Claude → A
Codex → B
DeepSeek → C
```

## Presentation-order randomization

The orchestration must use a run/reviewer shuffle mechanism rather than fixed provider ordering.

Tests should inject a deterministic fake shuffler rather than rely on probabilistic assertions.

## Per-stage model selection

Verify one provider can use:

```text
proposal model A
review model B
synthesis model C
```

without provider-specific orchestration branches.

## Parallel execution

Independent proposal calls should execute concurrently.

## Quorum

```text
3 success → success
2 success → success + warning
1 success → failure
```

## Rate limit

A rate-limited provider is retried according to policy before quorum is evaluated.

## Authentication failure

An authentication failure is not repeatedly retried.

## Run budget

When run token/call/duration policy forbids another invocation:

```text
13 RUN_BUDGET_EXCEEDED
```

must prevent additional model work.

## Provider budget

When one provider exceeds its configured budget:

```text
14 PROVIDER_BUDGET_EXCEEDED
```

must prevent further calls to that provider according to policy.

## Synthesizer fallback

If the first synthesizer participant fails, the next configured eligible provider/model is attempted.

## Final-plan gate

An invalid or evidence-inconsistent `final-plan.json` must never be published as a valid Markdown plan.

## Retention policy

Given more than `keepRuns` or runs older than `maxAgeDays`, `conclave prune --dry-run` must select the correct runs without deleting them.

A real prune must delete only the selected runs and their corresponding snapshot refs.

---

# 51. Integration test repositories

Integration tests create disposable Git repositories under the system temporary
directory. Test setup owns the minimal repository content required by each
scenario; the source tree does not retain a shared sample repository fixture.

Integration tests should verify the workflow rather than attempt to mathematically prove architectural quality.

At minimum verify:

- snapshot creation;
- workspace creation;
- provider execution;
- structured output;
- evidence resolution;
- final rendering;
- original repository isolation.

---

# 52. Definition of Done — V1

V1 is complete when this works:

```bash
conclave plan \
  --id SOFT-DELETE-001 \
  --directory ./sample-app \
  --prompt "Add soft deletion to Customer" \
  --json
```

and all of the following are true:

1. one immutable repository snapshot is created;
2. the snapshot is pinned under `refs/conclave/runs/<run-key>`;
3. the snapshot remains reachable after provider workspaces are deleted;
4. all providers work against the same `SnapshotSha`;
5. dirty working-tree state can be included deterministically when requested;
6. the user's real working tree and index are not used as provider workspaces;
7. the original logical repository state is captured before execution;
8. the same logical state is asserted after execution and workspace cleanup;
9. mutation of the original repository produces `ORIGINAL_REPOSITORY_MUTATED`;
10. providers run in isolated disposable workspaces;
11. providers may build and test inside those workspaces;
12. workspaces are deleted by default after the run;
13. `--keep-workspaces` preserves them for debugging;
14. initial proposals are independent;
15. internal artifacts are JSON, not Markdown;
16. proposal JSON is schema-validated;
17. repository evidence is deterministically checked;
18. `repository_fact` and `architectural_reasoning` remain distinct validation categories;
19. evidence paths must resolve against `SnapshotSha`;
20. implementation `targets` represent future state and are not evidence-gated by existence;
21. proposal aliases are opaque and randomized per run;
22. proposal presentation order is randomized for reviewers and synthesis;
23. providers do not review their own proposals;
24. reviews are anonymous and schema-validated;
25. review evidence is validated;
26. provider/model selection is configurable per Conclave stage;
27. synthesis does not use majority voting;
28. synthesis can use a different model from the proposal model within the same provider;
29. synthesizer failure has a fallback path;
30. `final-plan.json` is deterministically validated;
31. unresolved disagreements are preserved;
32. invalid evidence cannot silently become accepted fact;
33. Markdown is produced only by deterministic rendering;
34. usage/tokens are persisted per provider/model/stage;
35. run-level and provider-level resource budgets are enforced;
36. provider failures are classified;
37. quorum is evaluated after retry policy;
38. run retention is configurable;
39. `conclave prune` removes expired run artifacts and their snapshot refs together;
40. retained runs retain their snapshot refs;
41. stdout returns stable machine-readable JSON;
42. the calling agent can locate the final plan using only the CLI response.

---

# 53. V2 — `conclave review`

After `plan` is stable:

```bash
conclave review \
  --id EXPENSE-CATEGORIES-001 \
  --directory . \
  --plan <approved-plan>
```

Possible flow:

```text
Implementation snapshot
        │
        ▼
isolated provider workspaces
        │
   ┌────┼────┐
   ▼    ▼    ▼
Claude Codex DeepSeek
   │    │    │
   └────┼────┘
        ▼
structured implementation reviews
        │
        ▼
deterministic evidence validation
        │
        ▼
final compliance report
```

Questions answered:

```text
Does the implementation comply with the approved plan?
Were project invariants preserved?
Were required tests implemented?
Did implementation introduce undocumented architectural changes?
```

Do not mix this into V1.

---

# 54. V3 — MCP

Later expose the stable Conclave capabilities as MCP tools:

```text
conclave_plan
conclave_get_run
conclave_get_plan
conclave_review
```

The internal architecture should remain unchanged.

The CLI and MCP server become two transports over the same application layer.

---

# 55. Final V1 architecture

```text
                         CODING AGENT
                              │
                         conclave plan
                              │
                              ▼
                    ┌─────────────────┐
                    │   Conclave CLI   │
                    └────────┬────────┘
                             │
                             ▼
               Capture OriginalRepositoryState
                             │
                             ▼
                    Repository Snapshot
                       SnapshotSha
                             │
                             ▼
              refs/conclave/runs/<run-key>
                             │
              ┌──────────────┼──────────────┐
              ▼              ▼              ▼
        Claude workspace  Codex workspace  DeepSeek workspace
              │              │              │
          stage model      stage model     stage model
              │              │              │
              ▼              ▼              ▼
         Proposal K7      Proposal M2      Proposal Q9
            JSON             JSON             JSON
              │              │              │
              └──────────────┼──────────────┘
                             ▼
                   Deterministic Evidence
                        Validation
                             │
                             ▼
                       Cross Review
                 no self-review + shuffled
                             │
                             ▼
                   Deterministic Evidence
                        Validation
                             │
                             ▼
                 Budget / Fallback Checks
                             │
                             ▼
                  Synthesizer Participant
                    provider + model
                             │
                             ▼
                       FinalPlan.json
                             │
                             ▼
                   Deterministic Plan Gate
                             │
                             ▼
                     Markdown Renderer
                             │
                             ▼
                 implementation-plan.md
                             │
                             ▼
                   Delete Workspaces
                      by default
                             │
                             ▼
               Verify OriginalRepositoryState
                             │
                   ┌─────────┴─────────┐
                   ▼                   ▼
                 SAME               CHANGED
                   │                   │
                   ▼                   ▼
             successful run      EXIT 12 + alert
                   │
                   ▼
              Retained artifacts
             + retained snapshot ref
                   │
                   ▼
             retention / prune
                   │
                   ▼
                  Human
                  Review
```

---

# 56. Design principle

Conclave must not become another general-purpose agent framework.

It should remain a small, deterministic orchestration and validation layer around independently reasoning models.

Its core responsibility is:

```text
capture the user's repository state
      ↓
freeze the code into a retained snapshot
      ↓
pin the snapshot for auditability
      ↓
isolate the models
      ↓
let them investigate/build/test
      ↓
collect structured claims
      ↓
verify repository evidence
      ↓
cross-review independent proposals
      ↓
resolve only what evidence supports
      ↓
preserve unresolved disagreement
      ↓
enforce resource budgets
      ↓
validate the final plan
      ↓
render a human-reviewable artifact
      ↓
delete disposable workspaces
      ↓
prove the original repository was not changed
      ↓
retain audit artifacts until prune
```

The LLM is responsible for proposing architecture and reasoning.

Conclave is responsible for:

- preserving a reproducible repository snapshot;
- isolating provider execution;
- enforcing structured contracts;
- determining whether repository claims are structurally admissible;
- distinguishing observed evidence from intended future implementation targets;
- controlling resource consumption;
- preserving anonymity and review neutrality;
- ensuring the user's original repository remains logically unchanged;
- retaining and pruning audit artifacts and snapshot refs as one lifecycle;
- ensuring the resulting planning artifact satisfies the contract.

That separation is the central invariant of the design.
