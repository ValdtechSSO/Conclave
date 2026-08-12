using Conclave.Core;
using Conclave.Validation;

namespace Conclave.Validation.Tests;

public sealed class ValidationTests
{
    [Fact]
    public async Task Missing_evidence_file_is_invalid_but_missing_create_target_is_allowed()
    {
        var proposal = ValidProposal();
        proposal.Claims.Add(new() { Id = "FACT-1", Kind = ClaimKind.RepositoryFact, Statement = "Missing", Evidence = [new() { File = "src/Missing.cs", Symbol = "Missing" }] });
        proposal.ImplementationSteps[0].Targets = [new() { Path = "src/Future.cs", Operation = TargetOperation.Create }];
        var structural = new ArtifactValidator().ValidateProposal(proposal);
        var evidence = await new EvidenceValidator(new FakeReader(new Dictionary<string, string>())).ValidateAsync(proposal, Snapshot(), CancellationToken.None);
        Assert.True(structural.IsValid);
        Assert.False(evidence.IsValid);
        Assert.Contains(evidence.Issues, x => x.Code == "EVIDENCE_FILE_MISSING");
        Assert.DoesNotContain(structural.Issues, x => x.Code.Contains("EVIDENCE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_symbol_is_detected_and_reasoning_needs_no_evidence()
    {
        var proposal = ValidProposal();
        proposal.Claims =
        [
            new() { Id = "FACT", Kind = ClaimKind.RepositoryFact, Statement = "fact", Evidence = [new() { File = "src/Real.cs", Symbol = "Absent" }] },
            new() { Id = "REASON", Kind = ClaimKind.ArchitecturalReasoning, Statement = "reason" }
        ];
        var result = await new EvidenceValidator(new FakeReader(new Dictionary<string, string> { ["src/Real.cs"] = "class Real {}" })).ValidateAsync(proposal, Snapshot(), CancellationToken.None);
        Assert.Equal(1, result.TotalRepositoryClaims);
        Assert.Contains(result.Issues, x => x.Code == "EVIDENCE_SYMBOL_MISSING");
        Assert.DoesNotContain(result.Issues, x => x.Location == "REASON");
    }

    [Fact]
    public void Decision_linkage_and_step_completeness_are_enforced()
    {
        var proposal = ValidProposal();
        proposal.Decisions.Add(new() { Id = "D1", Statement = "decision", SupportedBy = ["UNKNOWN"] });
        proposal.ImplementationSteps[0].Tests.Clear();
        var result = new ArtifactValidator().ValidateProposal(proposal);
        Assert.Contains(result.Issues, x => x.Code == "DECISION_CLAIM_MISSING");
        Assert.Contains(result.Issues, x => x.Code == "STEP_TESTS_REQUIRED");
    }

    [Fact]
    public void Parser_recovers_json_from_code_fence_and_rejects_unknown_fields()
    {
        var parser = new ArtifactParser();
        var valid = parser.Parse<ProposalArtifact>("```json\n" + System.Text.Json.JsonSerializer.Serialize(ValidProposal(), ConclaveJson.Options) + "\n```");
        Assert.NotNull(valid.Artifact);
        var invalid = parser.Parse<ProposalArtifact>("""{"summary":"x","claims":[],"decisions":[],"implementationSteps":[],"risks":[],"alternatives":[],"openQuestions":[],"extra":1}""");
        Assert.Null(invalid.Artifact);
    }

    [Fact]
    public void Parser_applies_the_authoritative_json_schema()
    {
        var incomplete = """{"summary":"x","claims":[],"decisions":[],"implementationSteps":[],"risks":[],"alternatives":[]}""";
        var parsed = new ArtifactParser().Parse<ProposalArtifact>(incomplete, Path.Combine(FindAssets(), "schemas", "proposal.schema.json"));
        Assert.Null(parsed.Artifact);
        Assert.Contains("openQuestions", parsed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Final_plan_assumptions_must_remain_visible_as_open_questions()
    {
        var plan = new FinalPlanArtifact
        {
            Goal = "goal",
            Claims = [new() { Id = "A1", Kind = ClaimKind.Assumption, Statement = "Unknown deployment constraint" }],
            ImplementationSteps = [new() { Id = "S1", Targets = [new() { Path = "src/New.cs", Operation = TargetOperation.Create }], Changes = "change", Reason = "reason", Tests = ["test"] }]
        };
        var result = new ArtifactValidator().ValidateFinalPlan(plan);
        Assert.Contains(result.Issues, x => x.Code == "ASSUMPTION_NOT_SURFACED");
        plan.OpenQuestions.Add("Unknown deployment constraint");
        Assert.DoesNotContain(new ArtifactValidator().ValidateFinalPlan(plan).Issues, x => x.Code == "ASSUMPTION_NOT_SURFACED");
    }

    private static ProposalArtifact ValidProposal() => new()
    {
        Summary = "summary",
        ImplementationSteps = [new() { Id = "S1", Targets = [new() { Path = "src/New.cs", Operation = TargetOperation.Create }], Changes = "change", Reason = "reason", Tests = ["test"] }]
    };

    private static RepositorySnapshot Snapshot() => new("key", ".", "a", "b", "refs/conclave/runs/key", SnapshotMode.Head, false, false);

    private static string FindAssets()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "schemas"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException();
    }

    private sealed class FakeReader(Dictionary<string, string> files) : IRepositoryContentReader
    {
        public Task<(bool Exists, string? Content)> ReadTextAsync(RepositorySnapshot snapshot, string repositoryRelativePath, CancellationToken cancellationToken) =>
            Task.FromResult(files.TryGetValue(repositoryRelativePath, out var value) ? (true, (string?)value) : (false, null));
    }
}
