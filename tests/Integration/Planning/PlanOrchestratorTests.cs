using System.Text.Json;
using Conclave.Planning;
using Conclave.Planning.Features.Plan;
using Conclave.Planning.Infrastructure;

namespace Conclave.Planning.IntegrationTests;

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
        var repositoryContentWasEmbedded = false;
        var phaseArtifactsWereEmbedded = false;
        IModelAdapter Adapter(string id) => new ScriptedModelAdapter(id, async (request, token) =>
        {
            if (request.Stage == ConclaveStage.Proposal)
            {
                repositoryContentWasEmbedded |= request.Prompt.Contains("fixture marker", StringComparison.Ordinal);
                if (Interlocked.Increment(ref proposalCalls) == 2) proposalGate.TrySetResult();
                await proposalGate.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                return Success(request, Proposal(id));
            }
            if (request.Stage == ConclaveStage.Review)
            {
                phaseArtifactsWereEmbedded |= request.Prompt.Contains("proposal from", StringComparison.Ordinal);
                var files = Directory.GetFiles(Path.Combine(request.WorkingDirectory, ".conclave-input"), "proposal-*.json")
                    .Where(x => !x.EndsWith("-validation.json", StringComparison.Ordinal)).ToArray();
                selfProposalLeak |= files.Any(x => File.ReadAllText(x).Contains($"proposal from {id}", StringComparison.Ordinal));
                var aliases = files.Select(x => Path.GetFileNameWithoutExtension(x)!).ToList();
                var review = new ReviewArtifact
                {
                    Summary = "independent review",
                    ProposalAliases = aliases,
                    StrongestIdeas = ["keep evidence"],
                    UnresolvedDisagreements = [$"unresolved concern reported by {id}"]
                };
                if (id == "p1") review.ProposalAliases = ["wrong-but-recoverable"];
                if (id != "p2") return Success(request, review);
                review.StrongestIdeas.Add("second useful idea");
                var malformed = JsonSerializer.Serialize(review, ConclaveJson.Options)
                    .Replace("\"keep evidence\",", "\"keep evidence\"", StringComparison.Ordinal);
                return SuccessContent(request, malformed);
            }
            phaseArtifactsWereEmbedded |= request.Prompt.Contains("proposal from", StringComparison.Ordinal) || request.Prompt.Contains("independent review", StringComparison.Ordinal);
            if (id == "p1") return new(request.Participant, request.Stage, false, ProviderFailureKind.ProcessCrash, null, new(), TimeSpan.Zero, 1, "synthetic failure");
            var catalog = JsonSerializer.Deserialize<List<DisagreementCatalogEntry>>(
                File.ReadAllText(Path.Combine(request.WorkingDirectory, ".conclave-input", "disagreement-catalog.json")),
                ConclaveJson.Options)!;
            var plan = FinalPlan();
            plan.CouncilDisagreements = [new() { SourceIds = catalog.Select(x => x.Id).ToList(), Summary = "Paraphrased combined concern" }];
            return Success(request, plan);
        });

        var adapters = new Dictionary<string, IModelAdapter>(StringComparer.OrdinalIgnoreCase) { ["p1"] = Adapter("p1"), ["p2"] = Adapter("p2") };
        var snapshots = new GitRepositoryService(_process);
        var workspaceService = new GitProviderWorkspaceService(_process);
        var store = new FileRunStore(_home);
        var orchestrator = new PlanOrchestrator(configuration, adapters, snapshots, workspaceService, store, new ArtifactParser(), new ArtifactValidator(), new EvidenceValidator(snapshots), new MarkdownPlanRenderer(), new BudgetManager(configuration), new RandomShuffler(), FindPlanAssets());
        var result = await orchestrator.ExecuteAsync(new ConclaveRequest("FULL-001", _fixture, "Add a feature", SnapshotMode.Head, null, Scope: ["README.md"]), CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Equal(2, result.ProposalCount);
        Assert.Equal(2, result.ReviewCount);
        Assert.False(selfProposalLeak);
        Assert.False(repositoryContentWasEmbedded);
        Assert.False(phaseArtifactsWereEmbedded);
        Assert.Contains(result.Stages, x => x.Provider == "p1" && x.Stage == "synthesis" && !x.Success);
        Assert.Contains(result.Stages, x => x.Provider == "p2" && x.Stage == "synthesis" && x.Success);
        Assert.True(File.Exists(result.PlanPath));
        Assert.Contains("## 19. Conclave Execution Metadata", await File.ReadAllTextAsync(result.PlanPath!));
        Assert.Empty(Directory.GetDirectories(Path.Combine(result.RunPath, "workspaces")));
        var mapping = await store.ReadJsonAsync<Dictionary<string, string>>(result.RunId, "private/proposal-author-map.json", CancellationToken.None);
        Assert.NotNull(mapping);
        Assert.All(mapping!, pair => Assert.DoesNotContain(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase));
        var storedReviews = Directory.GetFiles(Path.Combine(result.RunPath, "reviews"), "review-*.json")
            .Select(path => JsonSerializer.Deserialize<ReviewArtifact>(File.ReadAllText(path), ConclaveJson.Options)!)
            .ToArray();
        Assert.Equal(2, storedReviews.Length);
        Assert.All(storedReviews.SelectMany(x => x.ProposalAliases), alias => Assert.DoesNotContain("proposal-", alias, StringComparison.Ordinal));
        var disagreementCatalog = await store.ReadJsonAsync<List<DisagreementCatalogEntry>>(result.RunId, "reviews/disagreement-catalog.json", CancellationToken.None);
        Assert.NotNull(disagreementCatalog);
        Assert.Equal(2, disagreementCatalog!.Count);
        Assert.All(disagreementCatalog, entry => Assert.Matches("^R[0-9a-f]{7}-D001$", entry.Id));
        var renderedPlan = await File.ReadAllTextAsync(result.PlanPath!);
        Assert.Contains("Paraphrased combined concern", renderedPlan, StringComparison.Ordinal);
        Assert.All(disagreementCatalog, entry => Assert.Contains(entry.Id, renderedPlan, StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning => warning.Contains("p2 review: structured JSON repaired locally", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning => warning.Contains("p1 review: proposal aliases were normalized locally", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(result.RunPath, "logs", "p2-review-0.repaired.json")));
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
        var orchestrator = new PlanOrchestrator(configuration, new Dictionary<string, IModelAdapter> { ["solo"] = adapter }, snapshots, workspaces, store, new ArtifactParser(), new ArtifactValidator(), new EvidenceValidator(snapshots), new MarkdownPlanRenderer(), new BudgetManager(configuration), new RandomShuffler(), FindPlanAssets());
        var result = await orchestrator.ExecuteAsync(new ConclaveRequest("MUTATION-001", _fixture, "Add feature", SnapshotMode.Head, null, ["solo"], DevelopmentMode: true, Scope: ["README.md"]), CancellationToken.None);
        Assert.Equal("failed", result.Status);
        Assert.Equal(ConclaveExitCode.OriginalRepositoryMutated, result.ExitCode);
        Assert.Equal(0, result.ReviewCount);
        Assert.True(File.Exists(Path.Combine(_fixture, "unexpected.txt")));
        await snapshots.DeleteSnapshotRefAsync(_fixture, result.SnapshotRef!, CancellationToken.None);
    }

    [Fact]
    public async Task Progress_reports_heartbeats_while_a_provider_is_still_running()
    {
        var configuration = ConfigurationLoader.Defaults();
        configuration.HomePath = _home;
        configuration.Providers.Clear();
        configuration.Providers["solo"] = Provider("solo");
        configuration.SynthesisFallback = [new() { Provider = "solo", Model = "solo-syn" }];
        var adapter = new ScriptedModelAdapter("solo", async (request, token) =>
        {
            request.Activity?.Invoke(new("scoped_analysis_started", "provider is analyzing the scoped task"));
            await Task.Delay(TimeSpan.FromMilliseconds(60), token);
            return request.Stage == ConclaveStage.Proposal ? Success(request, Proposal("solo")) : Success(request, FinalPlan());
        });
        var progress = new RecordingProgressSink();
        var snapshots = new GitRepositoryService(_process);
        var workspaces = new GitProviderWorkspaceService(_process);
        var store = new FileRunStore(_home);
        var orchestrator = new PlanOrchestrator(configuration, new Dictionary<string, IModelAdapter> { ["solo"] = adapter }, snapshots, workspaces, store, new ArtifactParser(), new ArtifactValidator(), new EvidenceValidator(snapshots), new MarkdownPlanRenderer(), new BudgetManager(configuration), new RandomShuffler(), FindPlanAssets(), progress, TimeSpan.FromMilliseconds(10));

        var result = await orchestrator.ExecuteAsync(new ConclaveRequest("PROGRESS-001", _fixture, "Add feature", SnapshotMode.Head, null, ["solo"], DevelopmentMode: true, Scope: ["README.md"]), CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Contains(progress.Updates, x => x.Phase == "proposal" && x.Provider == "solo" && x.Status == ConclaveProgressStatus.Started);
        Assert.Contains(progress.Updates, x => x.Phase == "proposal" && x.Provider == "solo" && x.ActivityCode == "task_assigned" && x.Message.Contains("suggested paths", StringComparison.Ordinal));
        Assert.Contains(progress.Updates, x => x.Phase == "proposal" && x.Provider == "solo" && x.ActivityCode == "scoped_analysis_started");
        Assert.Contains(progress.Updates, x => x.Phase == "proposal" && x.Provider == "solo" && x.Status == ConclaveProgressStatus.Running && x.ElapsedSeconds > 0);
        Assert.Contains(progress.Updates, x => x.Phase == "proposal" && x.Provider == "solo" && x.Status == ConclaveProgressStatus.Succeeded);
        Assert.Contains(progress.Updates, x => x.Phase == "run" && x.Status == ConclaveProgressStatus.Succeeded);
        await snapshots.DeleteSnapshotRefAsync(_fixture, result.SnapshotRef!, CancellationToken.None);
    }

    [Fact]
    public async Task Run_requires_recommended_starting_paths_or_repository_root_opt_in()
    {
        var configuration = ConfigurationLoader.Defaults();
        configuration.HomePath = _home;
        configuration.Providers.Clear();
        configuration.Providers["solo"] = Provider("solo");
        configuration.SynthesisFallback = [new() { Provider = "solo", Model = "solo-syn" }];
        var snapshots = new GitRepositoryService(_process);
        var orchestrator = new PlanOrchestrator(
            configuration,
            new Dictionary<string, IModelAdapter> { ["solo"] = new ScriptedModelAdapter("solo", (_, _) => throw new InvalidOperationException("Provider must not run.")) },
            snapshots,
            new GitProviderWorkspaceService(_process),
            new FileRunStore(_home),
            new ArtifactParser(),
            new ArtifactValidator(),
            new EvidenceValidator(snapshots),
            new MarkdownPlanRenderer(),
            new BudgetManager(configuration),
            new RandomShuffler(),
            FindPlanAssets());

        var error = await Assert.ThrowsAsync<ConclaveException>(() => orchestrator.ExecuteAsync(
            new ConclaveRequest("NO-SCOPE-001", _fixture, "Add feature", SnapshotMode.Head, null, ["solo"], DevelopmentMode: true),
            CancellationToken.None));

        Assert.Equal(ConclaveExitCode.InvalidRequest, error.ExitCode);
        Assert.Contains("starting paths", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(_home, "runs", "NO-SCOPE-001")));
    }

    [Fact]
    public async Task Every_physical_retry_is_metered_and_retained()
    {
        var configuration = ConfigurationLoader.Defaults();
        configuration.HomePath = _home;
        configuration.Providers.Clear();
        configuration.Providers["solo"] = Provider("solo");
        configuration.SynthesisFallback = [new() { Provider = "solo", Model = "solo-syn" }];
        configuration.Retry.ProcessCrashAttempts = 1;
        var proposalAttempts = 0;
        var adapter = new ScriptedModelAdapter("solo", (request, _) =>
        {
            if (request.Stage == ConclaveStage.Proposal && Interlocked.Increment(ref proposalAttempts) == 1)
                return Task.FromResult(new ModelExecutionResult(request.Participant, request.Stage, false, ProviderFailureKind.ProcessCrash, null, new(11, null, 2, 0.03m, "USD"), TimeSpan.FromMilliseconds(10), 1, "synthetic crash"));
            return Task.FromResult(request.Stage == ConclaveStage.Proposal ? Success(request, Proposal("solo")) : Success(request, FinalPlan()));
        });
        var snapshots = new GitRepositoryService(_process);
        var store = new FileRunStore(_home);
        var orchestrator = new PlanOrchestrator(
            configuration,
            new Dictionary<string, IModelAdapter> { ["solo"] = adapter },
            snapshots,
            new GitProviderWorkspaceService(_process),
            store,
            new ArtifactParser(),
            new ArtifactValidator(),
            new EvidenceValidator(snapshots),
            new MarkdownPlanRenderer(),
            new BudgetManager(configuration),
            new RandomShuffler(),
            FindPlanAssets());

        var result = await orchestrator.ExecuteAsync(
            new ConclaveRequest("RETRY-AUDIT-001", _fixture, "Add feature", SnapshotMode.Head, null, ["solo"], DevelopmentMode: true, Scope: ["README.md"]),
            CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Equal(2, proposalAttempts);
        Assert.Equal(2, result.Stages.Count(x => x.Stage == "proposal"));
        Assert.Contains(result.Stages, x => x.Stage == "proposal" && !x.Success && x.Usage.Cost == 0.03m);
        Assert.Equal(43, result.Usage.KnownTokens);
        Assert.True(File.Exists(Path.Combine(result.RunPath, "logs", "solo-proposal-structured-0-attempt-1.log")));
        Assert.True(File.Exists(Path.Combine(result.RunPath, "logs", "solo-proposal-structured-0-attempt-2.log")));
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

    private static ModelExecutionResult SuccessContent(ModelRequest request, string content) =>
        new(request.Participant, request.Stage, true, ProviderFailureKind.None, content, new(10, null, 5), TimeSpan.FromMilliseconds(10), 0, null);

    private sealed class RecordingProgressSink : IConclaveProgressSink
    {
        private readonly object _gate = new();
        public List<ConclaveProgressUpdate> Updates { get; } = [];
        public void Report(ConclaveProgressUpdate update)
        {
            lock (_gate) Updates.Add(update);
        }
    }

    private async Task Git(params string[] arguments)
    {
        var result = await _process.RunAsync(new ProcessRequest("git", arguments, _fixture), CancellationToken.None);
        Assert.True(result.ExitCode == 0, result.StandardError);
    }

    private static string FindPlanAssets()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var planAssets = Path.Combine(directory.FullName, "src", "Modules", "Planning", "Features", "Plan");
            if (Directory.Exists(Path.Combine(planAssets, "Schemas")) && Directory.Exists(Path.Combine(planAssets, "Prompts"))) return planAssets;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate Conclave assets.");
    }
}
