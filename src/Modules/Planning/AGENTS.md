# Planning module

## Purpose

Owns the complete lifecycle of evidence-backed planning runs: creation, display,
environment diagnosis, retention, and cleanup.

## Read before changing

- `module.contract.yml`
- `/domain/contexts/planning.md`
- `/architecture/decisions/ADR-001-compact-modular-architecture.md`

## Commands

- `dotnet test tests/Unit/Planning`
- `dotnet test tests/Integration/Planning`
- `./tools/scripts/validate-architecture.sh`

## Critical rules

- Use one snapshot SHA throughout a run.
- Proposal calls are independent and concurrent.
- Never include a provider's own proposal in its review input.
- Preserve validation warnings and disagreements through synthesis.
- Keep application behavior in the owning feature slice.
- Keep Git, providers, processes, persistence, and configuration under local infrastructure.
- Do not add empty directories, speculative abstractions, or technical modules.
