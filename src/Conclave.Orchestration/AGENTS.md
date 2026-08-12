# Planning orchestration

## Purpose

Coordinates snapshot, proposal, cross-review, synthesis, validation, rendering, and cleanup.

## Read before changing

- `module.contract.yml`
- `/domain/contexts/planning.md`
- `/architecture/decisions/ADR-001-project-and-slice-architecture.md`

## Commands

- `dotnet test Conclave.sln --filter Plan`
- `./tools/scripts/validate-architecture.sh`

## Critical rules

- Use one snapshot SHA throughout a run.
- Proposal calls are independent and concurrent.
- Never include a provider's own proposal in its review input.
- Preserve validation warnings and disagreements through synthesis.

