using System.Text.Json;
using Conclave.Core;
using Conclave.Infrastructure;

namespace Conclave.IntegrationTests;

public sealed class CliIntegrationTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "conclave-cli-integration-" + Guid.NewGuid().ToString("N"));
    private readonly ProcessRunner _process = new();

    public Task InitializeAsync() { Directory.CreateDirectory(_root); return Task.CompletedTask; }
    public Task DisposeAsync() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); return Task.CompletedTask; }

    [Fact]
    public async Task Cli_plan_returns_stable_json_and_publishes_validated_plan()
    {
        var repository = Path.Combine(_root, "repository");
        var home = Path.Combine(_root, "home");
        Directory.CreateDirectory(repository);
        await Git(repository, "init");
        await File.WriteAllTextAsync(Path.Combine(repository, "README.md"), "fixture\n");
        await Git(repository, "add", "README.md");
        await Git(repository, "-c", "user.name=Test", "-c", "user.email=test@local.invalid", "commit", "-m", "initial");
        var provider = Path.Combine(_root, "fake-provider.sh");
        await File.WriteAllTextAsync(provider, ProviderScript);
        var chmod = await _process.RunAsync(new ProcessRequest("/bin/chmod", ["700", provider], _root), CancellationToken.None);
        Assert.Equal(0, chmod.ExitCode);
        await File.WriteAllTextAsync(Path.Combine(repository, ".conclave.yaml"), Configuration(provider));

        var cliAssembly = typeof(Conclave.Cli.Program).Assembly.Location;
        var result = await _process.RunAsync(new ProcessRequest("dotnet",
        [
            cliAssembly, "plan", "--id", "CLI-001", "--directory", repository, "--prompt", "Add feature", "--providers", "p1,p2", "--json"
        ], repository, Environment: new Dictionary<string, string?> { ["CONCLAVE_HOME"] = home }, Timeout: TimeSpan.FromMinutes(2)), CancellationToken.None);

        Assert.True(result.ExitCode == 0, result.StandardError + result.StandardOutput);
        var run = JsonSerializer.Deserialize<RunResult>(result.StandardOutput, ConclaveJson.Options);
        Assert.NotNull(run);
        Assert.Equal("completed", run!.Status);
        Assert.Equal(2, run.ProposalCount);
        Assert.Equal(2, run.ReviewCount);
        Assert.True(File.Exists(run.PlanPath));
        Assert.StartsWith(Path.Combine(home, "runs"), run.RunPath, StringComparison.Ordinal);
        await Git(repository, "update-ref", "-d", run.SnapshotRef!);
    }

    private async Task Git(string path, params string[] arguments)
    {
        var result = await _process.RunAsync(new ProcessRequest("git", arguments, path), CancellationToken.None);
        Assert.True(result.ExitCode == 0, result.StandardError);
    }

    private static string Configuration(string provider) => $$"""
providers:
  p1:
    enabled: true
    command: "{{provider}}"
    proposal:
      model: p1-proposal
    review:
      model: p1-review
    synthesis:
      model: p1-synthesis
  p2:
    enabled: true
    command: "{{provider}}"
    proposal:
      model: p2-proposal
    review:
      model: p2-review
    synthesis:
      model: p2-synthesis
conclave:
  minimumProposalQuorum: 2
  minimumReviewQuorum: 2
synthesis:
  fallback:
    - provider: p1
      model: p1-synthesis
    - provider: p2
      model: p2-synthesis
""";

    private const string ProviderScript = """
#!/bin/sh
input=$(cat)
case "$input" in
  *"Phase: Proposal"*)
    printf '%s' '{"summary":"proposal","claims":[],"decisions":[],"implementationSteps":[{"id":"STEP","targets":[{"path":"src/New.cs","operation":"create","destination":null}],"changes":"create feature","reason":"meet request","tests":["unit test"]}],"risks":[],"alternatives":[],"openQuestions":[]}'
    ;;
  *"Phase: Review"*)
    alias=$(find .conclave-input -name 'proposal-*.json' ! -name '*-validation.json' -maxdepth 1 | head -n 1 | sed 's/.*proposal-//' | sed 's/.json//')
    printf '{"summary":"review","proposalAliases":["%s"],"claims":[],"incorrectAssumptions":[],"architecturalViolations":[],"missingInvariants":[],"complexityConcerns":[],"migrationRisks":[],"compatibilityProblems":[],"concurrencyConcerns":[],"securityConcerns":[],"missingTests":[],"rolloutRisks":[],"strongestIdeas":[],"unresolvedDisagreements":[]}' "$alias"
    ;;
  *)
    printf '%s' '{"goal":"implement feature","relevantArchitecture":[],"invariants":[],"architecturalDecisions":[],"domainChanges":[],"persistenceChanges":[],"apiChanges":[],"affectedComponents":["sample"],"implementationSteps":[{"id":"STEP","targets":[{"path":"src/New.cs","operation":"create","destination":null}],"changes":"create feature","reason":"meet request","tests":["unit test"]}],"migration":[],"testing":["unit test"],"observability":[],"security":[],"risks":[],"rejectedAlternatives":[],"councilDisagreements":[],"openQuestions":[],"claims":[]}'
    ;;
esac
""";
}
