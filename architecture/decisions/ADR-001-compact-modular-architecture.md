# ADR-001: Compact capability module with vertical feature slices

Status: accepted.

## Context

The V1 implementation separated the repository into global technical projects
for core contracts, orchestration, providers, Git, validation, and infrastructure.
That enforced some compile-time dependencies, but made one functional change span
many top-level locations and did not follow the repository manifesto's canonical
`src/Modules/{Module}/Features/{UseCase}` navigation path.

Conclave currently has one cohesive product capability: producing and managing
evidence-backed planning runs. Provider execution, Git snapshots, validation,
processes, and persistence are technical responsibilities of that capability,
not independent product modules.

## Decision

- `src/Modules/Planning` is the module root and owns its `AGENTS.md`, semantic
  contract, domain vocabulary, ports, use-case slices, and infrastructure.
- Application behavior starts in an explicit slice under `Features`: `CreatePlan`,
  `ShowRun`, `DiagnoseEnvironment`, or `PruneRuns`.
- `src/Hosts/Cli` parses console input, composes the module, and presents results;
  it does not own application behavior.
- Product code uses three assemblies: `Conclave.Planning`,
  `Conclave.Planning.Infrastructure`, and the `Conclave.Cli` host.
- Technical categories remain local to `Planning/Infrastructure`; they do not
  become modules.
- Directories, projects, abstractions, and shared components are created only
  when current code or an enforceable boundary requires them. Empty template
  directories and speculative assemblies are prohibited.
- Tests are organized first by test type and then by module or host.

## Consequences

An agent can navigate from intent to one module contract and then to one feature
slice. Prompts, schemas, validation, and orchestration for plan creation remain
colocated. Compile-time isolation is retained at the application/infrastructure
and module/host boundaries, while unnecessary technical assemblies disappear.

Future modules require independent functional vocabulary, ownership, contracts,
or lifecycle; anticipated reuse alone is insufficient.
