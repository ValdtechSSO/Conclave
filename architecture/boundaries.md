# Boundaries

- `src/Modules/Planning` is the functional module boundary.
- `Conclave.Planning` contains domain vocabulary, contracts, and use-case slices; it depends on neither infrastructure nor hosts.
- `Conclave.Planning.Infrastructure` implements the module's technical ports for providers, Git, processes, persistence, and configuration.
- `Conclave.Cli` is the console host and composition root; application behavior does not originate there.
- Technical responsibilities are folders inside module infrastructure, not product modules.
- New projects and shared locations require an enforceable current boundary or at least two current consumers.
