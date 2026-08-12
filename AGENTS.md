# Conclave repository

## Purpose

Conclave produces implementation plans by orchestrating independent model providers against one retained Git snapshot and validating their repository evidence deterministically.

## Start here

- Read `architecture/system-overview.md` and `domain/global-invariants.md`.
- Application behavior starts under `Features/{UseCase}`.
- Provider, Git, process, and filesystem details stay behind their project abstractions.

## Authoritative commands

- Build: `dotnet build Conclave.sln`
- Test: `dotnet test Conclave.sln`
- CLI: `dotnet run --project src/Conclave.Cli -- --help`
- Architecture check: `./tools/scripts/validate-architecture.sh`

## Critical rules

- Never execute a provider in the user's original working tree.
- Every stage uses the same retained `SnapshotSha`.
- Repository facts require validated evidence; future implementation targets do not.
- A provider never reviews its own proposal.
- Markdown is rendered only from a validated final-plan JSON artifact.
- Never silently restore a repository changed during execution.

## Map

- `src/`: product projects
- `tests/`: tests mirroring product responsibilities
- `schemas/`: authoritative JSON contracts
- `prompts/`: shared stage prompts
- `architecture/`, `domain/`: human-maintained decisions and invariants
- `.agentic/`: policies, workflows, templates, and generated/runtime placeholders

## Prohibited operations

- Provider push or remote mutation
- Direct provider access to the original repository
- Logging credentials or provider authentication material
- Committing `.conclave.secrets.env` or copying it into provider workspaces
- Treating model agreement as repository evidence
