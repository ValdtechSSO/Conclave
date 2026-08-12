using System.Text.Json;
using Conclave.Core;
using Conclave.Infrastructure;
using Conclave.Orchestration.Features.Plan;
using Conclave.Providers;
using Conclave.Repository;
using Conclave.Validation;

namespace Conclave.Orchestration.Tests;

public sealed class PlanOrchestratorTests : IAsyncLifetime
{
    private readonly string _fixture = Path.Combine(Path.GetTempPath(), "conclave-plan-fixture-" + Guid.NewGuid().ToString("N"));
    private readonly string _home = Path.Combine(Path.GetTempPath(), "conclave-plan-home-" + Guid.NewGuid().ToString("N"));
    private readonly ProcessRunner _process = new();

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_fixture);
        await Git("init");
        await File.WriteAllTextAsync(Path.Combine(_fixture, "README.md"), "fixture marker\n");
        await Git("add", "README.md");
        await Git("-c", "user.name=Test", "-c", "user.email=test@local.invalid", "commit", "-m", "initial");
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_fixture)) Directory.Delete(_fixture, recursive: true);
        if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Full_pipeline_is_parallel_anonymous_isolated_and_uses_synthesis_fallback()
    {
        var configuration = ConfigurationLoader.Defaults();
        configuration.HomePath = _home;
        configuration.Providers.Clear();
        configuration.Providers["p1"] = Provider("p1");
        configuration.Providers["p2"] = Provider("p2");
        configuration.SynthesisFallback = [new() { Provider = "p1", Model = "p1-syn" }, new() { Provider = "p2", Model = "p2-syn" }];
        configuration.MinimumProposalQuorum = 2;
        configuration.MinimumReviewQuorum = 2;

        var proposalGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proposalCalls = 0;
        var selfProposalLeak = false;
        IModelAdapter Adapter(string id) => new ScriptedModelAdapter(id, async (request, token) =>
        {
            if (request.Stage == ConclaveStage.Proposal)
            {
                if (Interlocked.Increment(ref proposalCalls) == 2) proposalGate.TrySetResult();
                await proposalGate.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                return Success(request, Proposal(id));
            }
            if (request.Stage == ConclaveStage.Review)
            {
                var files = Directory.GetFiles(Path.Combine(request.WorkingDirectory, ".conclave-input"), "proposal-*.json")
                    .Where(x => !x.EndsWith("-validation.json", StringComparison.Ordinal)).ToArray();
                selfProposalLeak |= files.Any(x => File.ReadAllText(x).Contains($"proposal from {id}", StringComparison.Ordinal));
                var aliases = files.Select(x => Path.GetFileNameWithoutExtension(x)["proposal-".Length..]).ToList();
                return Success(request, new ReviewArtifact { Summary = "independent review", ProposalAliases = aliases, StrongestIdeas = ["keep evidence"] });
            }
            if (id == "p1") return new(request.Participant, request.Stage, false, ProviderFailureKind.ProcessCrash, null, new(), TimeSpan.Zero, 1, "synthetic failure");
            return Success(request, FinalPlan());
        });

        var adapters = new Dictionary<string, IModelAdapter>(StringComparer.OrdinalIgnoreCase) { ["p1"] = Adapter("p1"), ["p2"] = Adapter("p2") };
        var snapshots = new GitRepositoryService(_process);
        var workspaceService = new GitProviderWorkspaceService(_process);
        var store = new FileRunStore(_home);
        var orchestrator = new PlanOrchestrator(configuration, adapters, snapshots, workspaceService, store, new ArtifactParser(), new ArtifactValidator(), new EvidenceValidator(snapshots), new MarkdownPlanRenderer(), new BudgetManager(configuration), new RandomShuffler(), FindAssets());
        var result = await orchestrator.ExecuteAsync(new ConclaveRequest("FULL-001", _fixture, "Add a feature", SnapshotMode.Head, null), CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Equal(2, result.ProposalCount);
        Assert.Equal(2, result.ReviewCount);
        Assert.False(selfProposalLeak);
        Assert.Contains(result.Stages, x => x.Provider == "p1" && x.Stage == "synthesis" && !x.Success);
        Assert.Contains(result.Stages, x => x.Provider == "p2" && x.Stage == "synthesis" && x.Success);
        Assert.True(File.Exists(result.PlanPath));
        Assert.Contains("## 19. Conclave Execution Metadata", await File.ReadAllTextAsync(result.PlanPath!));
        Assert.Empty(Directory.GetDirectories(Path.Combine(result.RunPath, "workspaces")));
        var mapping = await store.ReadJsonAsync<Dictionary<string, string>>(result.RunId, "private/proposal-author-map.json", CancellationToken.None);
        Assert.NotNull(mapping);
        Assert.All(mapping!, pair => Assert.DoesNotContain(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase));
        var snapshot = new RepositorySnapshot(result.RunKey, _fixture, result.SnapshotSha!, result.SnapshotSha!, result.SnapshotRef!, SnapshotMode.Head, false, false);
        Assert.True(await snapshots.SnapshotRefMatchesAsync(snapshot, CancellationToken.None));
        await snapshots.DeleteSnapshotRefAsync(_fixture, result.SnapshotRef!, CancellationToken.None);
    }

    [Fact]
    public async Task Development_run_skips_impossible_self_review_and_detects_original_mutation_without_reverting()
    {
        var configuration = ConfigurationLoader.Defaults();
        configuration.HomePath = _home;
        configuration.Providers.Clear();
        configuration.Providers["solo"] = Provider("solo");
        configuration.SynthesisFallback = [new() { Provider = "solo", Model = "solo-syn" }];
        var adapter = new ScriptedModelAdapter("solo", (request, _) =>
        {
            if (request.Stage == ConclaveStage.Proposal)
            {
                File.WriteAllText(Path.Combine(_fixture, "unexpected.txt"), "mutation");
                return Task.FromResult(Success(request, Proposal("solo")));
            }
            return Task.FromResult(Success(request, FinalPlan()));
        });
        var snapshots = new GitRepositoryService(_process);
        var workspaces = new GitProviderWorkspaceService(_process);
        var store = new FileRunStore(_home);
        var orchestrator = new PlanOrchestrator(configuration, new Dictionary<string, IModelAdapter> { ["solo"] = adapter }, snapshots, workspaces, store, new ArtifactParser(), new ArtifactValidator(), new EvidenceValidator(snapshots), new MarkdownPlanRenderer(), new BudgetManager(configuration), new RandomShuffler(), FindAssets());
        var result = await orchestrator.ExecuteAsync(new ConclaveRequest("MUTATION-001", _fixture, "Add feature", SnapshotMode.Head, null, ["solo"], DevelopmentMode: true), CancellationToken.None);
        Assert.Equal("failed", result.Status);
        Assert.Equal(ConclaveExitCode.OriginalRepositoryMutated, result.ExitCode);
        Assert.Equal(0, result.ReviewCount);
        Assert.True(File.Exists(Path.Combine(_fixture, "unexpected.txt")));
        await snapshots.DeleteSnapshotRefAsync(_fixture, result.SnapshotRef!, CancellationToken.None);
    }

    private static ProviderConfiguration Provider(string id) => new()
    {
        Command = id,
        Proposal = new() { Model = id + "-prop" },
        Review = new() { Model = id + "-review" },
        Synthesis = new() { Model = id + "-syn" }
    };

    private static ProposalArtifact Proposal(string provider) => new()
    {
        Summary = $"proposal from {provider}",
        Claims = [new() { Id = $"FACT-{provider}", Kind = ClaimKind.RepositoryFact, Statement = "Fixture exists", Evidence = [new() { File = "README.md", Symbol = "fixture marker" }] }],
        Decisions = [new() { Id = $"DEC-{provider}", Statement = "Use a feature slice", SupportedBy = [$"FACT-{provider}"] }],
        ImplementationSteps = [new() { Id = $"STEP-{provider}", Targets = [new() { Path = $"src/{provider}/Feature.cs", Operation = TargetOperation.Create }], Changes = "Create feature", Reason = "Implement request", Tests = ["Add unit coverage"] }]
    };

    private static FinalPlanArtifact FinalPlan() => new()
    {
        Goal = "Implement the feature",
        RelevantArchitecture = ["Feature slices"],
        Invariants = ["Preserve behavior"],
        Claims = [new() { Id = "FINAL-FACT", Kind = ClaimKind.RepositoryFact, Statement = "Fixture exists", Evidence = [new() { File = "README.md", Symbol = "fixture marker" }] }],
        ArchitecturalDecisions = [new() { Id = "FINAL-DEC", Statement = "Use a feature slice", SupportedBy = ["FINAL-FACT"] }],
        AffectedComponents = ["sample"],
        ImplementationSteps = [new() { Id = "FINAL-STEP", Targets = [new() { Path = "src/NewFeature.cs", Operation = TargetOperation.Create }], Changes = "Create feature", Reason = "Meet goal", Tests = ["Unit test"] }],
        Testing = ["Run all tests"]
    };

    private static ModelExecutionResult Success<T>(ModelRequest request, T artifact) =>
        new(request.Participant, request.Stage, true, ProviderFailureKind.None, JsonSerializer.Serialize(artifact, ConclaveJson.Options), new(10, null, 5), TimeSpan.FromMilliseconds(10), 0, null);

    private async Task Git(params string[] arguments)
    {
        var result = await _process.RunAsync(new ProcessRequest("git", arguments, _fixture), CancellationToken.None);
        Assert.True(result.ExitCode == 0, result.StandardError);
    }

    private static string FindAssets()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "schemas")) && Directory.Exists(Path.Combine(directory.FullName, "prompts"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate Conclave assets.");
    }
}
