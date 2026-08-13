# Conclave

Conclave is a local .NET CLI that asks multiple model providers to investigate the same immutable Git snapshot, cross-review anonymous proposals, validate repository evidence, and render one implementation plan from validated structured data.

## Build and test

```bash
dotnet build Conclave.sln
dotnet test Conclave.sln
```

## Install as a global tool

From the root of a Conclave checkout:

```bash
dotnet pack src/Hosts/Cli/Conclave.Cli.csproj --configuration Release
dotnet tool install --global --add-source ./artifacts/packages Conclave.Cli
```

If Conclave is already installed, update it from the newly built package instead:

```bash
dotnet tool update --global --add-source ./artifacts/packages Conclave.Cli
```

The .NET global-tools directory must be on `PATH`. For Zsh, add this to
`~/.zshrc` and start a new shell:

```bash
export PATH="$HOME/.dotnet/tools:$PATH"
```

Verify the installation:

```bash
conclave --help
```

## Configure providers and credentials

The configured provider CLIs (`claude` and `codex`) must already be installed.
Both the OpenAI and DeepSeek logical providers use `codex exec`: OpenAI uses its
native provider, while DeepSeek uses its Responses API-compatible endpoint. No
separate CodeWhale installation is required.

For use across multiple repositories, keep all API keys in one user-wide file:

```bash
mkdir -p "$HOME/.conclave"
cp .conclave.secrets.env.example "$HOME/.conclave/secrets.env"
chmod 600 "$HOME/.conclave/secrets.env"
```

Edit `~/.conclave/secrets.env` and set the providers you use:

```dotenv
ANTHROPIC_API_KEY=your-anthropic-key
DEEPSEEK_API_KEY=your-deepseek-key
OPENAI_API_KEY=your-openai-key
```

`OPENAI_API_KEY` is optional when Codex already uses a ChatGPT login. Conclave
loads only these three names, never persists their values in run metadata, and
passes each provider only its own credential.

For DeepSeek, Conclave configures an invocation-local Codex provider with
`base_url=https://api.deepseek.com`, `wire_api=responses`, and
`env_key=DEEPSEEK_API_KEY`. The key remains in `~/.conclave/secrets.env`; it is
not copied to `~/.codex/config.toml`. Conclave also supplies bundled model
metadata for `deepseek-v4-flash`, disables Codex plugins, subagents, browser
tools, and automatic provider retries, and keeps the nested agent read-only.
This follows DeepSeek's official [Responses API guide](https://api-docs.deepseek.com/guides/responses_api/)
and [Codex integration guide](https://api-docs.deepseek.com/quick_start/agent_integrations/codex).

User configuration lives at `~/.conclave/config.yaml`. A target repository may
override it with `.conclave.yaml` and, when repository-specific credentials are
really needed, with an ignored `.conclave.secrets.env`. Precedence is:

1. An explicitly exported environment variable.
2. The target repository's `.conclave.secrets.env`.
3. The user-wide `~/.conclave/secrets.env`.

Never commit either a real secret file or credentials copied into another
configuration file.

Check the complete installation without printing credential values:

```bash
conclave doctor
```

### Models and cost controls

Conclave pins models instead of inheriting whatever expensive default a provider
CLI happens to select. The shipped defaults are `sonnet` for Claude,
`gpt-5.6-sol` for Codex, and `deepseek-v4-flash` for DeepSeek. Override all stages
for one run without editing configuration:

```bash
conclave plan ... \
  --models "claude=sonnet,codex=gpt-5.6-sol,deepseek=deepseek-v4-flash"
```

Models can also be pinned independently for proposal, review, and synthesis in
`~/.conclave/config.yaml` or a repository `.conclave.yaml`:

```yaml
providers:
  claude:
    timeoutSeconds: 360
    maxCostUsd: 0.25
    budget:
      maxTokens: 1000000
      maxDurationMinutes: 15
      maxCalls: 3
      maxCostUsd: 0.25
    proposal:
      model: sonnet
    review:
      model: sonnet
    synthesis:
      model: sonnet
  codex:
    timeoutSeconds: 360
    budget:
      maxTokens: 1000000
      maxDurationMinutes: 15
      maxCalls: 3
      maxCostUsd: 0.25
    proposal:
      model: gpt-5.6-sol
    review:
      model: gpt-5.6-sol
    synthesis:
      model: gpt-5.6-sol
  deepseek:
    timeoutSeconds: 600
    budget:
      maxTokens: 4000000
      maxDurationMinutes: 30
      maxCalls: 3
      maxCostUsd: 0.25
    proposal:
      model: deepseek-v4-flash
    review:
      model: deepseek-v4-flash
    synthesis:
      model: deepseek-v4-flash
budget:
  run:
    maxDurationMinutes: 45
    maxCalls: 7
    maxCostUsd: 0.50
  provider:
    maxTokens: 1000000
    maxDurationMinutes: 5
    maxCalls: 3
    maxCostUsd: 0.25
retry:
  rateLimitAttempts: 0
  timeoutAttempts: 0
  invalidStructuredOutputAttempts: 0
  processCrashAttempts: 0
search:
  maxSuggestedRoots: 20
```

Token ceilings are cumulative per provider and are never pooled across the run.
Configure them under `providers.<id>.budget.maxTokens`, next to that provider's
per-call `timeoutSeconds`. The shipped Codex allowance is 1,000,000 tokens with a
360-second timeout; the cheaper DeepSeek provider receives 4,000,000 tokens and
600 seconds. `budget.provider` is only the fallback for custom providers that do
not define their own `budget` block. Run-wide limits cover elapsed wall time,
call count, and reported USD cost, but not tokens. User configuration in
`~/.conclave/config.yaml` or a repository `.conclave.yaml` may override each
provider independently.

`--max-cost-usd 0.50` may lower the run cap for one invocation. Cost limits are
hard limits only when the provider CLI supports a native cap; otherwise Conclave
stops subsequent calls as soon as reported usage reaches the cap. Every attempt,
including failures and timeouts, is retained. Conservative defaults perform no
automatic paid retries.

Before considering a structured response invalid, Conclave can locally repair
an unambiguous missing comma between complete JSON values on adjacent lines. The
repair consumes no provider tokens, retains both original and repaired output,
emits `structured_output_repaired`, and still requires the authoritative schema
and all semantic/evidence validations to pass. Broader or ambiguous malformed
output remains invalid and follows the configured retry policy.

Evidence symbols are matched exactly. For C# `string_literal` references,
Conclave additionally resolves complete `{nameof(Identifier)}` interpolations
before matching, because their value is fixed at compile time. It does not guess
runtime interpolation results or accept fuzzy text matches.

Validation is deliberately tolerant of recoverable representation mistakes. A
symbol that cannot be matched inside an existing evidence file is annotated as
not deterministically verifiable instead of invalidating the artifact under the
default `evidence.unverifiablePolicy: annotate`. If another reference verifies
the same claim, a redundant missing/bad reference is only a warning. Conclave
also normalizes review aliases from the known inputs, removes unknown support
IDs and duplicate/unknown disagreement IDs, drops blank test entries, restores
assumptions to `openQuestions`, and supplies default empty collections when a
nonessential JSON property is omitted. Safe target hints written with Windows
separators or a trailing directory slash are canonicalized to repository-style
paths. These local changes consume no provider tokens and appear in run warnings.

Blocking validation is reserved for conditions that cannot be repaired without
guessing: unsafe evidence paths, repository claims whose cited files are all
absent, duplicate artifact IDs, unsafe or conflicting targets, steps with no
usable tests, missing final-plan steps, and cataloged disagreements omitted from
the synthesis. Set `evidence.unverifiablePolicy: fail` when unmatched symbols in
existing files must also block a run.

## Use from any repository

Run Conclave from a project directory, or pass that project with `--directory`:

```bash
cd /path/to/repository

conclave plan \
  --id SOFT-DELETE-001 \
  --directory "$PWD" \
  --prompt "Add soft deletion to Customer" \
  --scope "src/Customers,tests/Customers" \
  --providers "codex,deepseek" \
  --snapshot working-tree \
  --max-cost-usd 0.50 \
  --json
```

`--scope` is required and accepts a comma-separated list of repository-relative
files or directories. These are recommended starting paths, not a hard boundary.
Choose the locations most likely to own the requested behavior; callers do not
need to enumerate every dependency. Conclave validates and records the guidance
under `request/search-guide.json` and `request/search-guide.md`, but does not put
repository file contents into the prompt. Each provider uses read-only tools in
its isolated worktree, begins at the suggested paths, and may follow direct
dependencies, consumers, contracts, tests, or another concrete evidence gap
elsewhere in the retained snapshot. Speculative whole-repository crawling is
prohibited. Use `--whole-repository` only when starting at the root is an
intentional choice.

Anonymous proposals, reviews, and validation results are likewise stored under
the provider worktree's `.conclave-input` directory instead of being duplicated
inside later prompts. Review and synthesis agents read the artifacts required for
their phase with the same read-only tools.

Cross-review disagreements receive stable anonymous IDs in
`reviews/disagreement-catalog.json`. The final synthesis references every ID
exactly once, while its human-readable summary may paraphrase or group related
concerns. This makes disagreement preservation deterministic without requiring
models to reproduce review text verbatim.

Use `--snapshot working-tree` when current uncommitted changes must be included;
use `--snapshot head` for the committed `HEAD`. JSON output contains `planPath`,
which points to the validated Markdown implementation plan.

While a plan is running, Conclave writes live progress to `stderr`: snapshot and
workspace preparation, each provider's proposal/review/synthesis start and end,
deterministic validation, cleanup, and the final repository-integrity check.
Long-running provider calls emit a heartbeat every 10 seconds with elapsed time,
so a calling agent can distinguish active work from a stalled or silent command.
The final `--json` result remains the only content on `stdout` and is therefore
safe to parse. Use `--no-progress` only when live status is deliberately not
wanted.

Provider progress also includes a safe `activityCode` and description. Conclave
always reports the assigned task, process startup, prompt delivery, first
response bytes, validation, and process completion. When a provider exposes
public streaming events, Conclave maps them to activities such as
`scoped_analysis_started`, `response_streaming`, and `response_completed`. It
never copies reasoning text, response content, or provider diagnostics into the
activity feed. A silent one-shot provider can only be reported at the observable
process/heartbeat level; Conclave does not invent more specific activity.

For IDE and agent integrations, use `--progress-format jsonl`. Events are written
to `stderr` as one JSON object per line and are always retained at
`~/.conclave/runs/<run-id>/progress.jsonl`. A second terminal or IDE process can
read the retained stream at any time:

```bash
conclave show <run-id> --progress
```

Conclave deletes provider worktrees by default while retaining structured
artifacts and `refs/conclave/runs/<run-key>` for auditability. `conclave prune`
applies the configured run/ref retention lifecycle.

### Live-provider smoke tests

The automated test suite uses local fake providers and never consumes an API.
When a real-provider smoke test is explicitly authorized, use only Codex with
`gpt-5.6-terra` and DeepSeek with `deepseek-v4-flash`; Claude is excluded from
tests. The repository wrapper pins this policy and lowers the reported run-cost
cap:

```bash
./tools/scripts/smoke-test-live-providers.sh \
  smoke-001 \
  "src/Modules/Planning/Infrastructure/Providers,tests/Integration/Planning/ProviderTests.cs" \
  /path/to/smoke-request.md
```

This wrapper does execute paid providers. Do not run it automatically or repeat
it after a failure without explicit approval.

## Make a coding agent use Conclave

Codex reads repository instructions from `AGENTS.md`. Add the following block to
the root `AGENTS.md` of any project that should use Conclave:

```md
## Conclave planning

- Before implementing a non-trivial feature, migration, or architectural change,
  run `conclave doctor` and resolve any failed readiness check.
- Generate a plan for the current repository with:
  `conclave plan --id "<unique-run-id>" --directory "$PWD" --prompt "<complete-user-request>" --scope "<smallest-relevant-paths>" --providers "<two-needed-providers>" --models "<provider=model,...>" --snapshot working-tree --max-cost-usd 0.50 --progress-format jsonl --json`.
- Before invoking Conclave, inspect only enough repository structure to select
  the best starting paths. Prefer the owning component/feature directory; the
  providers may follow dependencies, consumers, contracts, and tests elsewhere
  when evidence requires it. Do not enumerate the complete repository or pass
  `--whole-repository` unless the user explicitly authorizes starting at root.
- Pin every selected provider model with `--models` or verified repository/user
  configuration. Never allow a provider CLI to choose an implicit default.
- Prefer two providers that satisfy quorum. Add a third only as an intentional
  fallback when its extra cost is justified.
- Use a unique, descriptive run ID. Use `--prompt-file` instead of `--prompt`
  when the request is long or already stored in a file.
- Keep live progress enabled. Relay meaningful phase/provider changes and
  `activityCode` values to the user, and use
  `conclave show <run-id> --progress` if the IDE loses the stderr stream. Treat
  activity as observable execution telemetry, never as private model reasoning.
- Do not automatically start a new run after a timeout, billing failure, crash,
  or budget failure. Report retained diagnostics and request approval before any
  additional paid execution.
- Never use Claude for a live-provider test. An explicitly authorized smoke test
  uses only `codex=gpt-5.6-terra` and
  `deepseek=deepseek-v4-flash`; automated tests use fake providers.
- Read the validated plan at the `planPath` returned in the JSON result.
- Implement every applicable phase of that plan; do not stop after plan
  generation unless the user requested planning only.
- Run the repository's required build, test, lint, and architecture checks after
  implementation.
- If Conclave fails, report the error instead of bypassing it silently.
- Do not use `--development` unless the user explicitly requests a
  single-provider development run.
```

For another coding agent, place the same policy in the repository instruction
file that agent supports. The agent only needs shell access to the globally
installed `conclave` command; provider credentials stay in the central secret
file and must not be copied into prompts or agent instructions.

A ready-to-copy version is available at
[`.agentic/templates/conclave-agent-instructions.md`](.agentic/templates/conclave-agent-instructions.md).

See [the system overview](architecture/system-overview.md), [global invariants](domain/global-invariants.md), and [the original implementation plan](docs/implementation-plans/conclave-implementation-plan.md).
