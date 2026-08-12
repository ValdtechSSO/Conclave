using System.Text.Json;
using Conclave.Core;
using Conclave.Validation;

namespace Conclave.Orchestration.Features.Plan;

public sealed class ConclaveException(ConclaveExitCode exitCode, string message) : Exception(message)
{
    public ConclaveExitCode ExitCode { get; } = exitCode;
}

public sealed class PlanOrchestrator
{
    private readonly ConclaveConfiguration _configuration;
    private readonly IReadOnlyDictionary<string, IModelAdapter> _adapters;
    private readonly IRepositorySnapshotService _snapshots;
    private readonly IProviderWorkspaceService _workspaces;
    private readonly IRunStore _store;
    private readonly ArtifactParser _parser;
    private readonly IArtifactValidator _artifacts;
    private readonly IEvidenceValidator _evidence;
    private readonly IPlanRenderer _renderer;
    private readonly IBudgetManager _budget;
    private readonly IShuffler _shuffler;
    private readonly string _assetsPath;
    private readonly object _resultGate = new();

    public PlanOrchestrator(
        ConclaveConfiguration configuration,
        IReadOnlyDictionary<string, IModelAdapter> adapters,
        IRepositorySnapshotService snapshots,
        IProviderWorkspaceService workspaces,
        IRunStore store,
        ArtifactParser parser,
        IArtifactValidator artifacts,
        IEvidenceValidator evidence,
        IPlanRenderer renderer,
        IBudgetManager budget,
        IShuffler shuffler,
        string assetsPath)
    {
        _configuration = configuration;
        _adapters = adapters;
        _snapshots = snapshots;
        _workspaces = workspaces;
        _store = store;
        _parser = parser;
        _artifacts = artifacts;
        _evidence = evidence;
        _renderer = renderer;
        _budget = budget;
        _shuffler = shuffler;
        _assetsPath = Path.GetFullPath(assetsPath);
    }

    public async Task<RunResult> ExecuteAsync(ConclaveRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        var providerIds = SelectProviders(request);
        var requiredProposals = request.DevelopmentMode ? 1 : _configuration.MinimumProposalQuorum;
        var requiredReviews = request.DevelopmentMode && providerIds.Count == 1 ? 0 : _configuration.MinimumReviewQuorum;
        if (providerIds.Count < requiredProposals)
            throw new ConclaveException(ConclaveExitCode.ProviderQuorumFailure, $"Selected providers ({providerIds.Count}) cannot satisfy proposal quorum ({requiredProposals}).");

        var runKey = Guid.NewGuid().ToString("N");
        var run = new RunResult
        {
            RunId = request.RunId,
            RunKey = runKey,
            RunPath = _store.GetRunPath(request.RunId),
            RepositoryPath = Path.GetFullPath(request.RepositoryPath),
            StartedAt = DateTimeOffset.UtcNow,
            KeepWorkspaces = request.KeepWorkspaces || _configuration.Retention.KeepWorkspaces,
            Providers = [.. providerIds]
        };
        if (File.Exists(Path.Combine(run.RunPath, "result.json")))
            throw new ConclaveException(ConclaveExitCode.InvalidRequest, $"Run '{request.RunId}' already exists.");

        await _store.InitializeAsync(request.RunId, cancellationToken);
        await _store.WriteTextAsync(request.RunId, "request/feature.md", request.FeaturePrompt, cancellationToken);
        await _store.WriteJsonAsync(request.RunId, "request/metadata.json", request, cancellationToken);

        OriginalRepositoryState? before = null;
        SharedGitState? sharedBefore = null;
        RepositorySnapshot? snapshot = null;
        var providerWorkspaces = new Dictionary<string, ProviderWorkspace>(StringComparer.OrdinalIgnoreCase);
        ConclaveException? failure = null;
        try
        {
            before = await _snapshots.CaptureStateAsync(request.RepositoryPath, cancellationToken);
            snapshot = await _snapshots.CreateAsync(request.RepositoryPath, runKey, request.SnapshotMode, cancellationToken);
            run.RepositoryPath = snapshot.RepositoryPath;
            run.SnapshotSha = snapshot.SnapshotSha;
            run.SnapshotRef = snapshot.SnapshotRef;
            await _store.WriteJsonAsync(request.RunId, "request/snapshot.json", snapshot, cancellationToken);

            foreach (var providerId in providerIds)
            {
                var path = Path.Combine(run.RunPath, "workspaces", providerId);
                providerWorkspaces[providerId] = await _workspaces.CreateAsync(snapshot, providerId, path, cancellationToken);
            }
            sharedBefore = await _snapshots.CaptureSharedGitStateAsync(request.RepositoryPath, cancellationToken);

            var proposals = await RunProposalsAsync(request, run, snapshot, providerWorkspaces, cancellationToken);
            run.ProposalCount = proposals.Count;
            if (proposals.Count < requiredProposals)
                throw new ConclaveException(ConclaveExitCode.ProviderQuorumFailure, $"Proposal quorum failed: {proposals.Count}/{requiredProposals} validated proposals.");
            if (proposals.Count < providerIds.Count) run.Warnings.Add($"Proposal quorum continued with {proposals.Count}/{providerIds.Count} providers.");

            var reviews = await RunReviewsAsync(request, run, snapshot, providerWorkspaces, proposals, cancellationToken);
            run.ReviewCount = reviews.Count;
            if (reviews.Count < requiredReviews)
                throw new ConclaveException(ConclaveExitCode.ProviderQuorumFailure, $"Review quorum failed: {reviews.Count}/{requiredReviews} validated reviews.");
            if (reviews.Count < providerIds.Count) run.Warnings.Add($"Review quorum continued with {reviews.Count}/{providerIds.Count} providers.");

            var finalPlan = await RunSynthesisAsync(request, run, snapshot, providerWorkspaces, proposals, reviews, cancellationToken);
            await _store.WriteJsonAsync(request.RunId, "synthesis/final-plan.json", finalPlan, cancellationToken);
            run.CompletedAt = DateTimeOffset.UtcNow;
            var markdown = _renderer.Render(finalPlan, run);
            await _store.WriteTextAsync(request.RunId, "synthesis/implementation-plan.md", markdown, cancellationToken);
            run.PlanPath = Path.Combine(run.RunPath, "synthesis", "implementation-plan.md");
            run.Status = "completed";
            run.ExitCode = ConclaveExitCode.Success;
        }
        catch (OperationCanceledException)
        {
            failure = new ConclaveException(ConclaveExitCode.Cancelled, "Run cancelled.");
        }
        catch (ConclaveException exception)
        {
            failure = exception;
        }
        catch (Exception exception)
        {
            failure = new ConclaveException(snapshot is null ? ConclaveExitCode.SnapshotFailure : ConclaveExitCode.WorkspaceFailure, exception.Message);
        }
        finally
        {
            if (snapshot is not null && !run.KeepWorkspaces)
            {
                foreach (var workspace in providerWorkspaces.Values)
                {
                    try { await _workspaces.RemoveAsync(snapshot, workspace, CancellationToken.None); }
                    catch (Exception exception) { run.Warnings.Add($"Workspace cleanup failed for {workspace.ProviderId}: {exception.Message}"); failure ??= new ConclaveException(ConclaveExitCode.WorkspaceFailure, "One or more provider workspaces could not be removed."); }
                }
            }

            if (before is not null)
            {
                try
                {
                    if (sharedBefore is not null)
                    {
                        var sharedAfter = await _snapshots.CaptureSharedGitStateAsync(request.RepositoryPath, CancellationToken.None);
                        if (sharedBefore != sharedAfter) failure ??= new ConclaveException(ConclaveExitCode.WorkspaceFailure, "Shared Git references, local configuration, or remotes changed during provider execution.");
                    }
                    var after = await _snapshots.CaptureStateAsync(request.RepositoryPath, CancellationToken.None);
                    if (before != after) failure = new ConclaveException(ConclaveExitCode.OriginalRepositoryMutated, "The original repository logical state changed during Conclave execution; no automatic revert was attempted.");
                }
                catch (Exception exception)
                {
                    failure ??= new ConclaveException(ConclaveExitCode.OriginalRepositoryMutated, $"Could not verify original repository integrity: {exception.Message}");
                }
            }

            if (snapshot is not null && !await _snapshots.SnapshotRefMatchesAsync(snapshot, CancellationToken.None))
                failure ??= new ConclaveException(ConclaveExitCode.SnapshotFailure, "Retained snapshot reference no longer resolves to the run snapshot.");

            if (failure is not null)
            {
                run.Status = "failed";
                run.ExitCode = failure.ExitCode;
                run.Warnings.Add(failure.Message);
            }
            run.CompletedAt ??= DateTimeOffset.UtcNow;
            await _store.WriteJsonAsync(request.RunId, "result.json", run, CancellationToken.None);
        }

        if (failure is not null) return run;

        if (!string.IsNullOrWhiteSpace(request.OutputPath))
        {
            try
            {
                var output = Path.GetFullPath(Path.IsPathRooted(request.OutputPath) ? request.OutputPath : Path.Combine(request.RepositoryPath, request.OutputPath));
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.Copy(run.PlanPath!, output, overwrite: true);
            }
            catch (Exception exception)
            {
                run.Status = "failed";
                run.ExitCode = ConclaveExitCode.WorkspaceFailure;
                run.Warnings.Add($"Validated plan was retained but optional publication failed: {exception.Message}");
                await _store.WriteJsonAsync(request.RunId, "result.json", run, CancellationToken.None);
            }
        }
        return run;
    }

    private async Task<List<ProposalRecord>> RunProposalsAsync(ConclaveRequest request, RunResult run, RepositorySnapshot snapshot, Dictionary<string, ProviderWorkspace> workspaces, CancellationToken cancellationToken)
    {
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        var aliasByProvider = workspaces.Keys.ToDictionary(x => x, _ => _shuffler.CreateAlias(aliases), StringComparer.OrdinalIgnoreCase);
        await _store.WriteJsonAsync(request.RunId, "private/proposal-author-map.json", aliasByProvider, cancellationToken);
        var tasks = workspaces.Select(pair => ExecuteProposalAsync(request, run, snapshot, pair.Value, aliasByProvider[pair.Key], cancellationToken));
        var results = await Task.WhenAll(tasks);
        return results.Where(x => x is not null).Cast<ProposalRecord>().ToList();
    }

    private async Task<ProposalRecord?> ExecuteProposalAsync(ConclaveRequest request, RunResult run, RepositorySnapshot snapshot, ProviderWorkspace workspace, string alias, CancellationToken cancellationToken)
    {
        await _workspaces.ResetAsync(workspace, cancellationToken);
        var schemaPath = await MaterializeCommonAsync(request, snapshot, workspace, ConclaveStage.Proposal, "proposal.schema.json", cancellationToken);
        var participant = Participant(workspace.ProviderId, ConclaveStage.Proposal);
        var prompt = BuildPrompt("proposal.md", request, snapshot, ConclaveStage.Proposal);
        var executed = await ExecuteAndParseAsync<ProposalArtifact>(run, workspace.ProviderId, new ModelRequest(request.RunId, ConclaveStage.Proposal, prompt, workspace.Path, schemaPath, participant), cancellationToken);
        if (executed.Artifact is null) return FailedProvider(run, workspace.ProviderId, executed.Error);
        var structural = _artifacts.ValidateProposal(executed.Artifact);
        var evidence = await _evidence.ValidateAsync(executed.Artifact, snapshot, cancellationToken);
        var validation = ValidationResults.Merge(structural, evidence);
        await _store.WriteJsonAsync(request.RunId, $"validation/proposal-{alias}-evidence.json", validation, cancellationToken);
        if (!Eligible(validation, request, run, $"proposal {alias}")) return FailedProvider(run, workspace.ProviderId, "Proposal validation failed.");
        await _store.WriteJsonAsync(request.RunId, $"proposals/proposal-{alias}.json", executed.Artifact, cancellationToken);
        return new ProposalRecord(workspace.ProviderId, participant, alias, executed.Artifact, validation);
    }

    private async Task<List<ReviewRecord>> RunReviewsAsync(ConclaveRequest request, RunResult run, RepositorySnapshot snapshot, Dictionary<string, ProviderWorkspace> workspaces, List<ProposalRecord> proposals, CancellationToken cancellationToken)
    {
        var tasks = workspaces.Select(async pair =>
        {
            var foreign = _shuffler.Shuffle(proposals.Where(x => !string.Equals(x.ProviderId, pair.Key, StringComparison.OrdinalIgnoreCase)));
            if (foreign.Count == 0) return null;
            await _workspaces.ResetAsync(pair.Value, cancellationToken);
            var schemaPath = await MaterializeCommonAsync(request, snapshot, pair.Value, ConclaveStage.Review, "review.schema.json", cancellationToken);
            foreach (var proposal in foreign)
            {
                await WriteInputJsonAsync(pair.Value.Path, $"proposal-{proposal.Alias}.json", proposal.Artifact, cancellationToken);
                await WriteInputJsonAsync(pair.Value.Path, $"proposal-{proposal.Alias}-validation.json", proposal.Validation, cancellationToken);
            }
            var participant = Participant(pair.Key, ConclaveStage.Review);
            var prompt = BuildPrompt("review.md", request, snapshot, ConclaveStage.Review);
            var executed = await ExecuteAndParseAsync<ReviewArtifact>(run, pair.Key, new ModelRequest(request.RunId, ConclaveStage.Review, prompt, pair.Value.Path, schemaPath, participant), cancellationToken);
            if (executed.Artifact is null) return FailedReviewProvider(run, pair.Key, executed.Error);
            var expected = foreign.Select(x => x.Alias).Order(StringComparer.Ordinal).ToArray();
            var actual = executed.Artifact.ProposalAliases.Order(StringComparer.Ordinal).ToArray();
            if (!expected.SequenceEqual(actual, StringComparer.Ordinal)) return FailedReviewProvider(run, pair.Key, "Review did not identify exactly the supplied anonymous proposals.");
            var structural = _artifacts.ValidateReview(executed.Artifact);
            var evidence = await _evidence.ValidateAsync(executed.Artifact, snapshot, cancellationToken);
            var validation = ValidationResults.Merge(structural, evidence);
            var reviewAlias = "R" + Guid.NewGuid().ToString("N")[..7];
            await _store.WriteJsonAsync(request.RunId, $"validation/review-{reviewAlias}-evidence.json", validation, cancellationToken);
            if (!Eligible(validation, request, run, $"review {reviewAlias}")) return FailedReviewProvider(run, pair.Key, "Review validation failed.");
            await _store.WriteJsonAsync(request.RunId, $"reviews/review-{reviewAlias}.json", executed.Artifact, cancellationToken);
            return new ReviewRecord(pair.Key, participant, reviewAlias, executed.Artifact, validation);
        });
        var results = await Task.WhenAll(tasks);
        return results.Where(x => x is not null).Cast<ReviewRecord>().ToList();
    }

    private async Task<FinalPlanArtifact> RunSynthesisAsync(ConclaveRequest request, RunResult run, RepositorySnapshot snapshot, Dictionary<string, ProviderWorkspace> workspaces, List<ProposalRecord> proposals, List<ReviewRecord> reviews, CancellationToken cancellationToken)
    {
        var proposalParticipants = proposals.Select(x => x.Participant).ToHashSet();
        var candidates = _configuration.SynthesisFallback
            .Where(x => workspaces.ContainsKey(x.Provider) && _adapters.ContainsKey(x.Provider))
            .OrderBy(x => proposalParticipants.Contains(new ParticipantIdentity(x.Provider, x.Model)) ? 1 : 0)
            .ToArray();
        var parsedButInvalid = false;
        var structuredFailure = false;
        foreach (var candidate in candidates)
        {
            var workspace = workspaces[candidate.Provider];
            await _workspaces.ResetAsync(workspace, cancellationToken);
            var schemaPath = await MaterializeCommonAsync(request, snapshot, workspace, ConclaveStage.Synthesis, "final-plan.schema.json", cancellationToken);
            var items = _shuffler.Shuffle(proposals.Select(x => (Name: $"proposal-{x.Alias}.json", Value: (object)x.Artifact))
                .Concat(reviews.Select(x => (Name: $"review-{x.Alias}.json", Value: (object)x.Artifact))));
            var order = 0;
            foreach (var item in items) await WriteInputJsonAsync(workspace.Path, $"{order++:D2}-{item.Name}", item.Value, cancellationToken);
            foreach (var proposal in proposals) await WriteInputJsonAsync(workspace.Path, $"validation-proposal-{proposal.Alias}.json", proposal.Validation, cancellationToken);
            foreach (var review in reviews) await WriteInputJsonAsync(workspace.Path, $"validation-review-{review.Alias}.json", review.Validation, cancellationToken);

            var participant = new ParticipantIdentity(candidate.Provider, candidate.Model);
            var prompt = BuildPrompt("synthesis.md", request, snapshot, ConclaveStage.Synthesis);
            var executed = await ExecuteAndParseAsync<FinalPlanArtifact>(run, candidate.Provider, new ModelRequest(request.RunId, ConclaveStage.Synthesis, prompt, workspace.Path, schemaPath, participant), cancellationToken);
            if (executed.Artifact is null) { structuredFailure |= executed.Error?.Contains("JSON", StringComparison.OrdinalIgnoreCase) == true || executed.Error?.Contains("schema", StringComparison.OrdinalIgnoreCase) == true || executed.Error?.Contains("structured", StringComparison.OrdinalIgnoreCase) == true; run.Warnings.Add($"Synthesis participant {candidate.Provider}/{candidate.Model} failed: {executed.Error}"); continue; }
            var structural = _artifacts.ValidateFinalPlan(executed.Artifact);
            var requiredDisagreements = reviews.SelectMany(x => x.Artifact.UnresolvedDisagreements).Distinct(StringComparer.Ordinal).ToArray();
            foreach (var disagreement in requiredDisagreements)
                if (!executed.Artifact.CouncilDisagreements.Contains(disagreement, StringComparer.Ordinal) && !executed.Artifact.OpenQuestions.Contains(disagreement, StringComparer.Ordinal))
                    structural.Issues.Add(new("DISAGREEMENT_DROPPED", $"Review disagreement was not preserved: {disagreement}", "finalPlan.councilDisagreements", EvidenceStatus.Invalid));
            var evidence = await _evidence.ValidateAsync(executed.Artifact, snapshot, cancellationToken);
            var validation = ValidationResults.Merge(structural, evidence);
            await _store.WriteJsonAsync(request.RunId, $"validation/final-plan-{candidate.Provider}-evidence.json", validation, cancellationToken);
            if (!Eligible(validation, request, run, $"final plan from {candidate.Provider}/{candidate.Model}")) { parsedButInvalid = true; continue; }
            return executed.Artifact;
        }
        if (parsedButInvalid) throw new ConclaveException(ConclaveExitCode.FinalPlanInvalid, "Synthesis produced final-plan data that failed deterministic validation.");
        if (structuredFailure) throw new ConclaveException(ConclaveExitCode.StructuredOutputInvalid, "Synthesis participants did not produce schema-valid structured output.");
        throw new ConclaveException(ConclaveExitCode.SynthesisFailure, "No synthesis participant produced a valid final plan.");
    }

    private async Task<(T? Artifact, string? Error)> ExecuteAndParseAsync<T>(RunResult run, string providerId, ModelRequest request, CancellationToken cancellationToken) where T : class
    {
        var adapter = _adapters[providerId];
        var structuredAttempts = _configuration.Retry.InvalidStructuredOutputAttempts + 1;
        string? lastError = null;
        for (var structuredAttempt = 0; structuredAttempt < structuredAttempts; structuredAttempt++)
        {
            var invocation = request with { IsRepair = structuredAttempt > 0, Prompt = structuredAttempt == 0 ? request.Prompt : request.Prompt + "\n\nREPAIR: Your previous response was invalid. Return only one JSON object matching the schema exactly." };
            var result = await ExecuteWithRetryAsync(adapter, invocation, cancellationToken);
            Record(run, result);
            await _store.WriteTextAsync(run.RunId, $"logs/{providerId}-{request.Stage.ToString().ToLowerInvariant()}-{structuredAttempt}.output", result.Content ?? "", cancellationToken);
            await _store.WriteTextAsync(run.RunId, $"logs/{providerId}-{request.Stage.ToString().ToLowerInvariant()}-{structuredAttempt}.log", result.Error ?? $"exit={result.ExitCode}; duration={result.Duration}", cancellationToken);
            if (!result.Success) return (null, result.Error ?? result.FailureKind.ToString());
            var parsed = _parser.Parse<T>(result.Content, request.OutputSchemaPath);
            if (parsed.Artifact is not null) return parsed;
            lastError = parsed.Error;
        }
        return (null, lastError ?? "Invalid structured output.");
    }

    private async Task<ModelExecutionResult> ExecuteWithRetryAsync(IModelAdapter adapter, ModelRequest request, CancellationToken cancellationToken)
    {
        var attempts = 0;
        while (true)
        {
            var decision = _budget.CanStart(request);
            if (!decision.Allowed) throw new ConclaveException(decision.ExitCode, decision.Reason ?? "Budget exceeded.");
            var result = await adapter.ExecuteAsync(request, cancellationToken);
            _budget.Record(result);
            if (result.Success) return result;
            var retries = result.FailureKind switch
            {
                ProviderFailureKind.RateLimit => _configuration.Retry.RateLimitAttempts,
                ProviderFailureKind.Timeout => _configuration.Retry.TimeoutAttempts,
                ProviderFailureKind.ProcessCrash => _configuration.Retry.ProcessCrashAttempts,
                _ => 0
            };
            if (attempts++ >= retries) return result;
            if (result.FailureKind == ProviderFailureKind.RateLimit)
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(5_000, 250 * Math.Pow(2, attempts))), cancellationToken);
        }
    }

    private void Record(RunResult run, ModelExecutionResult result)
    {
        lock (_resultGate)
        {
            run.Stages.Add(new StageRecord
            {
                Provider = result.Participant.ProviderId,
                Model = result.Participant.ModelId,
                Stage = result.Stage.ToString().ToLowerInvariant(),
                Success = result.Success,
                FailureKind = result.FailureKind,
                DurationSeconds = result.Duration.TotalSeconds,
                Usage = result.Usage,
                Error = result.Error
            });
            run.Usage += result.Usage;
        }
    }

    private bool Eligible(ValidationResult validation, ConclaveRequest request, RunResult run, string label)
    {
        lock (_resultGate)
            foreach (var issue in validation.Issues) run.Warnings.Add($"{label}: {issue.Code}: {issue.Message}");
        if (validation.Invalid > 0 || validation.Issues.Any(x => x.Status == EvidenceStatus.Invalid)) return false;
        var policy = request.EvidencePolicy ?? _configuration.EvidencePolicy;
        return policy == UnverifiablePolicy.Annotate || validation.Unverified == 0;
    }

    private async Task<string> MaterializeCommonAsync(ConclaveRequest request, RepositorySnapshot snapshot, ProviderWorkspace workspace, ConclaveStage stage, string schemaName, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(workspace.Path, ".conclave-input");
        Directory.CreateDirectory(directory);
        var brief = $"# Conclave brief\n\nRun ID: {request.RunId}\nSnapshot SHA: {snapshot.SnapshotSha}\nPhase: {stage.ToString().ToLowerInvariant()}\n\nFeature:\n{request.FeaturePrompt}\n\nRules:\n- Inspect only this disposable snapshot workspace.\n- Do not mutate Git remotes or shared refs.\n- Treat output-schema.json as authoritative.\n- Repository evidence is relative to snapshot {snapshot.SnapshotSha}.\n";
        await File.WriteAllTextAsync(Path.Combine(directory, "CONCLAVE.md"), brief, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "feature.md"), request.FeaturePrompt, cancellationToken);
        var schemaPath = Path.Combine(directory, "output-schema.json");
        File.Copy(Path.Combine(_assetsPath, "schemas", schemaName), schemaPath, overwrite: true);
        return schemaPath;
    }

    private string BuildPrompt(string promptName, ConclaveRequest request, RepositorySnapshot snapshot, ConclaveStage stage) =>
        $"You are participating in Conclave run {request.RunId} at immutable snapshot {snapshot.SnapshotSha}. Read .conclave-input/CONCLAVE.md and all supplied phase inputs.\n\n{File.ReadAllText(Path.Combine(_assetsPath, "prompts", promptName))}\n\nPhase: {stage}. Feature:\n{request.FeaturePrompt}";

    private static async Task WriteInputJsonAsync<T>(string workspacePath, string fileName, T value, CancellationToken cancellationToken)
    {
        var path = Path.Combine(workspacePath, ".conclave-input", fileName);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, ConclaveJson.Options), cancellationToken);
    }

    private ParticipantIdentity Participant(string providerId, ConclaveStage stage)
    {
        var provider = _configuration.Providers[providerId];
        return new ParticipantIdentity(providerId, provider.For(stage).Model);
    }

    private List<string> SelectProviders(ConclaveRequest request)
    {
        var requested = request.Providers is { Count: > 0 } ? request.Providers : _configuration.Providers.Where(x => x.Value.Enabled).Select(x => x.Key).ToArray();
        var selected = requested.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var providerId in selected)
            if (!_configuration.Providers.TryGetValue(providerId, out var provider) || !provider.Enabled || !_adapters.ContainsKey(providerId))
                throw new ConclaveException(ConclaveExitCode.ConfigurationError, $"Provider '{providerId}' is not configured and enabled.");
        if (selected.Count == 1 && !request.DevelopmentMode)
            throw new ConclaveException(ConclaveExitCode.ProviderQuorumFailure, "Single-provider runs require explicit development mode.");
        return selected;
    }

    private void ValidateRequest(ConclaveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RunId) || string.IsNullOrWhiteSpace(request.FeaturePrompt))
            throw new ConclaveException(ConclaveExitCode.InvalidRequest, "Run ID and feature prompt are required.");
        if (!Directory.Exists(request.RepositoryPath))
            throw new ConclaveException(ConclaveExitCode.InvalidRequest, $"Repository does not exist: {request.RepositoryPath}");
        foreach (var file in new[] { "proposal.schema.json", "review.schema.json", "final-plan.schema.json" })
            if (!File.Exists(Path.Combine(_assetsPath, "schemas", file))) throw new ConclaveException(ConclaveExitCode.ConfigurationError, $"Missing Conclave schema: {file}");
    }

    private static ProposalRecord? FailedProvider(RunResult run, string providerId, string? error)
    {
        lock (run) { if (!run.MissingProviders.Contains(providerId, StringComparer.OrdinalIgnoreCase)) run.MissingProviders.Add(providerId); if (!string.IsNullOrWhiteSpace(error)) run.Warnings.Add($"{providerId} proposal: {error}"); }
        return null;
    }

    private static ReviewRecord? FailedReviewProvider(RunResult run, string providerId, string? error)
    {
        lock (run) { if (!string.IsNullOrWhiteSpace(error)) run.Warnings.Add($"{providerId} review: {error}"); }
        return null;
    }

    private sealed record ProposalRecord(string ProviderId, ParticipantIdentity Participant, string Alias, ProposalArtifact Artifact, ValidationResult Validation);
    private sealed record ReviewRecord(string ProviderId, ParticipantIdentity Participant, string Alias, ReviewArtifact Artifact, ValidationResult Validation);
}
