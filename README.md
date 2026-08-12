# Conclave

Conclave is a local .NET CLI that asks multiple model providers to investigate the same immutable Git snapshot, cross-review anonymous proposals, validate repository evidence, and render one implementation plan from validated structured data.

## Build and test

```bash
dotnet build Conclave.sln
dotnet test Conclave.sln
```

## Use

```bash
dotnet run --project src/Conclave.Cli -- plan \
  --id SOFT-DELETE-001 \
  --directory /path/to/repository \
  --prompt "Add soft deletion to Customer" \
  --snapshot working-tree \
  --json
```

To create and install the global tool locally:

```bash
dotnet pack src/Conclave.Cli/Conclave.Cli.csproj --configuration Release
dotnet tool install --global --add-source ./artifacts/packages Conclave.Cli
conclave --help
```

The configured provider CLIs (`claude`, `codex`, and CodeWhale's DeepSeek provider by default) must already be installed and authenticated. The logical provider ID remains `deepseek`, while its current executable is `codewhale`. Run `conclave doctor` to inspect readiness. User configuration lives at `~/.conclave/config.yaml`; a target repository may override it with `.conclave.yaml`.

Provider API keys can be centralized in the ignored repository file `.conclave.secrets.env`:

```dotenv
ANTHROPIC_API_KEY=your-anthropic-key
DEEPSEEK_API_KEY=your-deepseek-key
OPENAI_API_KEY=your-openai-key
```

Copy `.conclave.secrets.env.example` to create it in another repository. Conclave loads only these three names, never persists their values in run metadata, excludes the ignored file from snapshots, and passes each provider only its own credential. A user-wide `~/.conclave/secrets.env` is also supported; a repository secret file takes precedence, while an explicitly exported environment variable has highest precedence. `OPENAI_API_KEY` is optional when Codex already uses a ChatGPT login.

`plan` deletes provider worktrees by default while retaining structured artifacts and `refs/conclave/runs/<run-key>` for auditability. `conclave prune` applies the configured run/ref retention lifecycle.

See [the system overview](architecture/system-overview.md), [global invariants](domain/global-invariants.md), and [the V1 implementation plan](docs/implementation-plans/conclave-implementation-plan.md).
