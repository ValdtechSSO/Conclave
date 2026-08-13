using System.Text.Json;
using System.Text.Json.Serialization;

namespace Conclave.Planning;

public enum SnapshotMode { Head, WorkingTree }
public enum ConclaveStage { Proposal, Review, Synthesis }
public enum ProviderFailureKind { None, Timeout, RateLimit, Authentication, Billing, ProcessCrash, InvalidStructuredOutput, ContextLimit, Cancelled, Unknown }
public enum PromptTransport { Stdin, TemporaryFile, Argument }
public enum JsonSchemaDialect { Authoritative, DraftAgnostic, OpenAiStrict }
public enum ClaimKind { RepositoryFact, ArchitecturalReasoning, Assumption, ExternalConstraint }
public enum EvidenceStatus { Verified, Unverified, Invalid, NotDeterministicallyVerifiable }
public enum TargetOperation { Create, Modify, Delete, Rename, Move, Generated }
public enum UnverifiablePolicy { Annotate, Fail }

public static class RepositoryPath
{
    public static bool IsSafeRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\0')) return false;
        var parts = path.Replace('\\', '/').Split('/');
        return parts.All(part => part.Length > 0 && part is not "." and not "..");
    }
}

public enum ConclaveExitCode
{
    Success = 0,
    InvalidRequest = 2,
    ProviderQuorumFailure = 3,
    WorkspaceFailure = 4,
    SynthesisFailure = 5,
    ConfigurationError = 6,
    Cancelled = 7,
    PlanEvidenceUnverifiable = 8,
    StructuredOutputInvalid = 9,
    SnapshotFailure = 10,
    FinalPlanInvalid = 11,
    OriginalRepositoryMutated = 12,
    RunBudgetExceeded = 13,
    ProviderBudgetExceeded = 14
}

public sealed record ConclaveRequest(
    string RunId,
    string RepositoryPath,
    string FeaturePrompt,
    SnapshotMode SnapshotMode,
    string? OutputPath,
    IReadOnlyList<string>? Providers = null,
    bool KeepWorkspaces = false,
    UnverifiablePolicy? EvidencePolicy = null,
    bool DevelopmentMode = false,
    IReadOnlyList<string>? Scope = null,
    bool WholeRepository = false);

public sealed record ParticipantIdentity(string ProviderId, string ModelId);

public sealed record UsageMetrics(
    long? InputTokens = null,
    long? CachedInputTokens = null,
    long? OutputTokens = null,
    decimal? Cost = null,
    string? Currency = null)
{
    [JsonIgnore]
    public long KnownTokens => (InputTokens ?? 0) + (OutputTokens ?? 0);

    public static UsageMetrics operator +(UsageMetrics left, UsageMetrics right) => new(
        Add(left.InputTokens, right.InputTokens),
        Add(left.CachedInputTokens, right.CachedInputTokens),
        Add(left.OutputTokens, right.OutputTokens),
        Add(left.Cost, right.Cost),
        left.Currency ?? right.Currency);

    private static long? Add(long? left, long? right) => left is null && right is null ? null : (left ?? 0) + (right ?? 0);
    private static decimal? Add(decimal? left, decimal? right) => left is null && right is null ? null : (left ?? 0) + (right ?? 0);
}

public sealed record ModelRequest(
    string RunId,
    ConclaveStage Stage,
    string Prompt,
    string WorkingDirectory,
    string OutputSchemaPath,
    ParticipantIdentity Participant,
    bool IsRepair = false,
    [property: JsonIgnore] Action<ProviderActivity>? Activity = null);

public sealed record ProviderActivity(string Code, string Message);

public sealed record ModelExecutionResult(
    ParticipantIdentity Participant,
    ConclaveStage Stage,
    bool Success,
    ProviderFailureKind FailureKind,
    string? Content,
    UsageMetrics Usage,
    TimeSpan Duration,
    int? ExitCode,
    string? Error);

public sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string? StandardInput = null,
    IReadOnlyDictionary<string, string?>? Environment = null,
    TimeSpan? Timeout = null,
    int MaxOutputCharacters = 4_000_000,
    [property: JsonIgnore] Action<ProcessActivity>? Activity = null);

public enum ProcessActivityKind { Started, InputDelivered, StandardOutput, StandardError, Exited }

public sealed record ProcessActivity(ProcessActivityKind Kind, string? Data = null);

public sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    bool TimedOut,
    bool Cancelled,
    bool OutputTruncated);

public sealed class EvidenceReference
{
    public string File { get; set; } = "";
    public string? Symbol { get; set; }
    public string Kind { get; set; } = "source";
}

public sealed class Claim
{
    public string Id { get; set; } = "";
    public ClaimKind Kind { get; set; }
    public string Statement { get; set; } = "";
    public List<EvidenceReference> Evidence { get; set; } = [];
}

public sealed class ArchitecturalDecision
{
    public string Id { get; set; } = "";
    public string Statement { get; set; } = "";
    public List<string> SupportedBy { get; set; } = [];
}

public sealed class ImplementationTarget
{
    public string Path { get; set; } = "";
    public TargetOperation Operation { get; set; }
    public string? Destination { get; set; }
}

public sealed class ImplementationStep
{
    public string Id { get; set; } = "";
    public List<ImplementationTarget> Targets { get; set; } = [];
    public string Changes { get; set; } = "";
    public string Reason { get; set; } = "";
    public List<string> Tests { get; set; } = [];
}

public interface IConclaveArtifact
{
    List<Claim> Claims { get; }
}

public sealed class ProposalArtifact : IConclaveArtifact
{
    public string Summary { get; set; } = "";
    public List<Claim> Claims { get; set; } = [];
    public List<ArchitecturalDecision> Decisions { get; set; } = [];
    public List<ImplementationStep> ImplementationSteps { get; set; } = [];
    public List<string> Risks { get; set; } = [];
    public List<string> Alternatives { get; set; } = [];
    public List<string> OpenQuestions { get; set; } = [];
}

public sealed class ReviewArtifact : IConclaveArtifact
{
    public string Summary { get; set; } = "";
    public List<string> ProposalAliases { get; set; } = [];
    public List<Claim> Claims { get; set; } = [];
    public List<string> IncorrectAssumptions { get; set; } = [];
    public List<string> ArchitecturalViolations { get; set; } = [];
    public List<string> MissingInvariants { get; set; } = [];
    public List<string> ComplexityConcerns { get; set; } = [];
    public List<string> MigrationRisks { get; set; } = [];
    public List<string> CompatibilityProblems { get; set; } = [];
    public List<string> ConcurrencyConcerns { get; set; } = [];
    public List<string> SecurityConcerns { get; set; } = [];
    public List<string> MissingTests { get; set; } = [];
    public List<string> RolloutRisks { get; set; } = [];
    public List<string> StrongestIdeas { get; set; } = [];
    public List<string> UnresolvedDisagreements { get; set; } = [];
}

public sealed class DisagreementCatalogEntry
{
    public string Id { get; set; } = "";
    public string Statement { get; set; } = "";
}

public sealed class CouncilDisagreement
{
    public List<string> SourceIds { get; set; } = [];
    public string Summary { get; set; } = "";
}

public sealed class FinalPlanArtifact : IConclaveArtifact
{
    public string Goal { get; set; } = "";
    public List<string> RelevantArchitecture { get; set; } = [];
    public List<string> Invariants { get; set; } = [];
    public List<ArchitecturalDecision> ArchitecturalDecisions { get; set; } = [];
    public List<string> DomainChanges { get; set; } = [];
    public List<string> PersistenceChanges { get; set; } = [];
    public List<string> ApiChanges { get; set; } = [];
    public List<string> AffectedComponents { get; set; } = [];
    public List<ImplementationStep> ImplementationSteps { get; set; } = [];
    public List<string> Migration { get; set; } = [];
    public List<string> Testing { get; set; } = [];
    public List<string> Observability { get; set; } = [];
    public List<string> Security { get; set; } = [];
    public List<string> Risks { get; set; } = [];
    public List<string> RejectedAlternatives { get; set; } = [];
    public List<CouncilDisagreement> CouncilDisagreements { get; set; } = [];
    public List<string> OpenQuestions { get; set; } = [];
    public List<Claim> Claims { get; set; } = [];
}

public sealed record ValidationIssue(string Code, string Message, string? Location = null, EvidenceStatus? Status = null);

public sealed class ValidationResult
{
    public List<ValidationIssue> Issues { get; } = [];
    public int TotalRepositoryClaims { get; set; }
    public int Verified { get; set; }
    public int Unverified { get; set; }
    public int Invalid { get; set; }
    public double EvidenceScore => TotalRepositoryClaims == 0 ? 1 : (double)Verified / TotalRepositoryClaims;
    public bool IsValid => Invalid == 0 && Issues.All(x => x.Status is not EvidenceStatus.Invalid);
}

public sealed record OriginalRepositoryState(
    string HeadOid,
    string IndexTreeOid,
    string TrackedWorkingTreeDiffHash,
    string UntrackedContentHash);

public sealed record SharedGitState(string ReferencesHash, string LocalConfigurationHash, string RemotesHash);

public sealed record RepositorySnapshot(
    string RunKey,
    string RepositoryPath,
    string BaseHead,
    string SnapshotSha,
    string SnapshotRef,
    SnapshotMode SnapshotMode,
    bool IncludedWorkingTreeChanges,
    bool IncludedUntrackedFiles,
    bool IncludedIgnoredFiles = false);

public sealed record ProviderWorkspace(string ProviderId, string Path, string SnapshotSha);

public sealed class StageModelConfiguration
{
    public string Model { get; set; } = "default";
    public List<string> Arguments { get; set; } = [];
}

public sealed class ProviderConfiguration
{
    public bool Enabled { get; set; } = true;
    public string Command { get; set; } = "";
    public PromptTransport PromptTransport { get; set; } = PromptTransport.Stdin;
    public JsonSchemaDialect JsonSchemaDialect { get; set; } = JsonSchemaDialect.Authoritative;
    public int TimeoutSeconds { get; set; } = 360;
    public decimal MaxCostUsd { get; set; } = 0.25m;
    public List<string> ProbeArguments { get; set; } = ["--version"];
    public List<string> ProbeFailurePatterns { get; set; } = [];
    public string? CredentialEnvironmentVariable { get; set; }
    public bool CredentialRequired { get; set; }
    [JsonIgnore]
    public string? CredentialValue { get; set; }
    public bool SupportsSchemaConstrainedOutput { get; set; }
    public bool ReportsUsageAfterCall { get; set; }
    public bool SupportsRealTimeTokenLimit { get; set; }
    public StageModelConfiguration Proposal { get; set; } = new();
    public StageModelConfiguration Review { get; set; } = new();
    public StageModelConfiguration Synthesis { get; set; } = new();
    public BudgetLimit? Budget { get; set; }

    public StageModelConfiguration For(ConclaveStage stage) => stage switch
    {
        ConclaveStage.Proposal => Proposal,
        ConclaveStage.Review => Review,
        ConclaveStage.Synthesis => Synthesis,
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };
}

public sealed class BudgetLimit
{
    public long MaxTokens { get; set; } = 1_000_000;
    public int MaxDurationMinutes { get; set; } = 10;
    public int MaxCalls { get; set; } = int.MaxValue;
    public decimal MaxCostUsd { get; set; } = 0.50m;
}

public sealed class RunBudgetLimit
{
    public int MaxDurationMinutes { get; set; } = 45;
    public int MaxCalls { get; set; } = int.MaxValue;
    public decimal MaxCostUsd { get; set; } = 0.50m;
}

public sealed class RetryConfiguration
{
    public int RateLimitAttempts { get; set; }
    public int TimeoutAttempts { get; set; }
    public int InvalidStructuredOutputAttempts { get; set; }
    public int ProcessCrashAttempts { get; set; }
}

public sealed class RepositorySearchConfiguration
{
    public int MaxSuggestedRoots { get; set; } = 20;
}

public sealed class RetentionConfiguration
{
    public int KeepRuns { get; set; } = 20;
    public int MaxAgeDays { get; set; } = 30;
    public bool KeepWorkspaces { get; set; }
}

public sealed class SynthesisParticipant
{
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
}

public sealed class ConclaveConfiguration
{
    public string HomePath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".conclave");
    public Dictionary<string, ProviderConfiguration> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int MinimumProposalQuorum { get; set; } = 2;
    public int MinimumReviewQuorum { get; set; } = 2;
    public List<SynthesisParticipant> SynthesisFallback { get; set; } = [];
    public UnverifiablePolicy EvidencePolicy { get; set; } = UnverifiablePolicy.Annotate;
    public RunBudgetLimit RunBudget { get; set; } = new() { MaxCalls = 7 };
    public BudgetLimit ProviderBudget { get; set; } = new() { MaxTokens = 1_000_000, MaxDurationMinutes = 5, MaxCalls = 3, MaxCostUsd = 0.25m };
    public bool AbortOnBudgetExceeded { get; set; } = true;
    public RetentionConfiguration Retention { get; set; } = new();
    public RetryConfiguration Retry { get; set; } = new();
    public RepositorySearchConfiguration Search { get; set; } = new();
}

public sealed record RepositorySearchGuide(
    IReadOnlyList<string> SuggestedRoots,
    int MatchingFileCount);

public sealed class StageRecord
{
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string Stage { get; set; } = "";
    public bool Success { get; set; }
    public ProviderFailureKind FailureKind { get; set; }
    public double DurationSeconds { get; set; }
    public UsageMetrics Usage { get; set; } = new();
    public string? Error { get; set; }
}

public sealed class RunResult
{
    public string RunId { get; set; } = "";
    public string RunKey { get; set; } = "";
    public string Status { get; set; } = "running";
    public ConclaveExitCode ExitCode { get; set; }
    public string? SnapshotSha { get; set; }
    public string? SnapshotRef { get; set; }
    public string? RepositoryPath { get; set; }
    public string? PlanPath { get; set; }
    public string RunPath { get; set; } = "";
    public int ProposalCount { get; set; }
    public int ReviewCount { get; set; }
    public List<string> Providers { get; set; } = [];
    public List<string> MissingProviders { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<StageRecord> Stages { get; set; } = [];
    public UsageMetrics Usage { get; set; } = new();
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public bool KeepWorkspaces { get; set; }
}

public sealed record BudgetDecision(bool Allowed, ConclaveExitCode ExitCode = ConclaveExitCode.Success, string? Reason = null)
{
    public static BudgetDecision Allow() => new(true);
    public static BudgetDecision Deny(ConclaveExitCode code, string reason) => new(false, code, reason);
}

public enum ConclaveProgressStatus
{
    Started,
    Running,
    Retrying,
    Succeeded,
    Failed
}

public sealed record ConclaveProgressUpdate(
    DateTimeOffset Timestamp,
    string RunId,
    string Phase,
    ConclaveProgressStatus Status,
    string Message,
    string? Provider = null,
    double? ElapsedSeconds = null,
    string? ActivityCode = null);

public interface IConclaveProgressSink
{
    void Report(ConclaveProgressUpdate update);
}

public static class ConclaveJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken);
}

public interface IModelAdapter
{
    string Id { get; }
    Task<ModelExecutionResult> ExecuteAsync(ModelRequest request, CancellationToken cancellationToken);
    Task<(bool Available, string Detail)> ProbeAsync(CancellationToken cancellationToken);
}

public interface IRepositorySnapshotService
{
    Task<OriginalRepositoryState> CaptureStateAsync(string repositoryPath, CancellationToken cancellationToken);
    Task<SharedGitState> CaptureSharedGitStateAsync(string repositoryPath, CancellationToken cancellationToken);
    Task<RepositorySnapshot> CreateAsync(string repositoryPath, string runKey, SnapshotMode mode, CancellationToken cancellationToken);
    Task<bool> SnapshotRefMatchesAsync(RepositorySnapshot snapshot, CancellationToken cancellationToken);
    Task DeleteSnapshotRefAsync(string repositoryPath, string snapshotRef, CancellationToken cancellationToken);
}

public interface IProviderWorkspaceService
{
    Task<ProviderWorkspace> CreateAsync(RepositorySnapshot snapshot, string providerId, string path, CancellationToken cancellationToken);
    Task ResetAsync(ProviderWorkspace workspace, CancellationToken cancellationToken);
    Task RemoveAsync(RepositorySnapshot snapshot, ProviderWorkspace workspace, CancellationToken cancellationToken);
    Task PruneMetadataAsync(string repositoryPath, CancellationToken cancellationToken);
}

public interface IRepositoryContentReader
{
    Task<(bool Exists, string? Content)> ReadTextAsync(RepositorySnapshot snapshot, string repositoryRelativePath, CancellationToken cancellationToken);
}

public interface IRepositorySearchGuideBuilder
{
    Task<RepositorySearchGuide> BuildAsync(RepositorySnapshot snapshot, IReadOnlyList<string> suggestedRoots, RepositorySearchConfiguration limits, CancellationToken cancellationToken);
}

public interface IArtifactValidator
{
    ValidationResult ValidateProposal(ProposalArtifact artifact);
    ValidationResult ValidateReview(ReviewArtifact artifact);
    ValidationResult ValidateFinalPlan(FinalPlanArtifact artifact, IReadOnlyCollection<string>? requiredDisagreementIds = null);
}

public interface IEvidenceValidator
{
    Task<ValidationResult> ValidateAsync(IConclaveArtifact artifact, RepositorySnapshot snapshot, CancellationToken cancellationToken);
}

public interface IPlanRenderer
{
    string Render(FinalPlanArtifact plan, RunResult run);
}

public interface IBudgetManager
{
    BudgetDecision CanStart(ModelRequest request);
    void Record(ModelExecutionResult result);
}

public interface IRunStore
{
    string GetRunPath(string runId);
    Task InitializeAsync(string runId, CancellationToken cancellationToken);
    Task WriteJsonAsync<T>(string runId, string relativePath, T value, CancellationToken cancellationToken);
    Task WriteTextAsync(string runId, string relativePath, string value, CancellationToken cancellationToken);
    Task<T?> ReadJsonAsync<T>(string runId, string relativePath, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListRunIdsAsync(CancellationToken cancellationToken);
}

public interface IShuffler
{
    IReadOnlyList<T> Shuffle<T>(IEnumerable<T> values);
    string CreateAlias(ISet<string> existingAliases);
}
