# Conclave repository

## Purpose

Conclave produces implementation plans by orchestrating independent model providers against one retained Git snapshot and validating their repository evidence deterministically.

## Start here

- Read `architecture/system-overview.md` and `domain/global-invariants.md`.
- Application behavior starts under `src/Modules/{Module}/Features/{FeatureArea}`.
- Read the owning module's `module.contract.yml` and `AGENTS.md` before changing it.
- Provider, Git, process, and filesystem details stay in the module's local infrastructure behind its ports.

## Authoritative commands

- Build: `dotnet build Conclave.sln`
- Test: `dotnet test Conclave.sln`
- CLI: `dotnet run --project src/Hosts/Cli -- --help`
- Architecture check: `./tools/scripts/validate-architecture.sh`
- Run all .NET build, test, and pack commands outside the filesystem sandbox;
  MSBuild and VSTest require local IPC that is unreliable inside it.

## Critical rules

- Never execute a provider in the user's original working tree.
- Every stage uses the same retained `SnapshotSha`.
- Repository facts require validated evidence; future implementation targets do not.
- A provider never reviews its own proposal.
- Markdown is rendered only from a validated final-plan JSON artifact.
- Never silently restore a repository changed during execution.
- Provider search guidance must identify the best repository-relative starting
  paths. These paths are recommendations, not an evidence boundary: providers
  may follow direct dependencies, consumers, contracts, and tests elsewhere in
  the retained snapshot when necessary. Speculative whole-repository crawling
  remains prohibited.
- Provider model IDs must be visible in configuration or CLI arguments; never
  rely on a provider's implicit default model.
- A timeout, process crash, billing failure, or invalid artifact must not trigger
  a new paid provider call unless the configured retry count explicitly allows
  it. Conservative defaults use zero retries.
- Preserve every provider attempt, usage report, cost report, and progress event,
  including failed and timed-out attempts.
- Live progress belongs on stderr and in the retained `progress.jsonl`; stable
  command results belong on stdout.
- Provider activity must describe only assigned work and observable public
  events. Never expose, reconstruct, or invent private model reasoning.
- Provider phases must never invoke Conclave recursively, call another provider,
  or delegate to a subagent.
- Do not add empty template directories, speculative abstractions, technical
  modules, or assemblies without an enforced current boundary.
- Automated tests use fake providers and must never consume provider APIs. Any
  explicitly authorized live-provider smoke test uses only Codex
  `gpt-5.6-terra` and DeepSeek `deepseek-v4-flash`; never invoke Claude for a
  test. Use `tools/scripts/smoke-test-live-providers.sh` so this selection cannot
  be inherited from implicit defaults.

## Map

- `src/Modules/Planning/`: planning module, semantic contract, feature slices, and local infrastructure
- `src/Hosts/Cli/`: console transport and composition root
- `tests/{Unit,Integration,EndToEnd}/`: tests organized by type, then module or host
- `architecture/`, `domain/`: human-maintained decisions and invariants
- `.agentic/`: policies, workflows, templates, and generated/runtime placeholders

## Prohibited operations

- Provider push or remote mutation
- Direct provider access to the original repository
- Logging credentials or provider authentication material
- Committing `.conclave.secrets.env` or copying it into provider workspaces
- Treating model agreement as repository evidence
