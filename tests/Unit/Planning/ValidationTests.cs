using Conclave.Planning;
using Conclave.Planning.Features.Plan;

namespace Conclave.Planning.UnitTests;

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
    public async Task Missing_symbol_is_annotated_as_unverifiable_and_reasoning_needs_no_evidence()
    {
        var proposal = ValidProposal();
        proposal.Claims =
        [
            new() { Id = "FACT", Kind = ClaimKind.RepositoryFact, Statement = "fact", Evidence = [new() { File = "src/Real.cs", Symbol = "Absent" }] },
            new() { Id = "REASON", Kind = ClaimKind.ArchitecturalReasoning, Statement = "reason" }
        ];
        var result = await new EvidenceValidator(new FakeReader(new Dictionary<string, string> { ["src/Real.cs"] = "class Real {}" })).ValidateAsync(proposal, Snapshot(), CancellationToken.None);
        Assert.Equal(1, result.TotalRepositoryClaims);
        Assert.Equal(1, result.Unverified);
        Assert.Contains(result.Issues, x => x.Code == "EVIDENCE_SYMBOL_UNVERIFIABLE" && x.Status == EvidenceStatus.NotDeterministicallyVerifiable);
        Assert.DoesNotContain(result.Issues, x => x.Location == "REASON");
    }

    [Fact]
    public async Task String_literal_evidence_resolves_csharp_nameof_interpolation_deterministically()
    {
        var proposal = ValidProposal();
        proposal.Claims =
        [
            new()
            {
                Id = "FACT",
                Kind = ClaimKind.RepositoryFact,
                Statement = "Items null is rejected.",
                Evidence =
                [
                    new()
                    {
                        File = "src/DnetList.razor",
                        Symbol = "requires the 'Items' parameters to be specified and non-null.",
                        Kind = "string_literal"
                    }
                ]
            }
        ];
        var source = "throw new InvalidOperationException($\"{GetType()} requires the '{nameof(Items)}' parameters to be specified and non-null.\");";

        var result = await new EvidenceValidator(new FakeReader(new Dictionary<string, string> { ["src/DnetList.razor"] = source }))
            .ValidateAsync(proposal, Snapshot(), CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(1, result.Verified);
        Assert.DoesNotContain(result.Issues, issue => issue.Code == "EVIDENCE_SYMBOL_UNVERIFIABLE");
    }

    [Fact]
    public async Task String_literal_evidence_does_not_guess_runtime_interpolation_values()
    {
        var proposal = ValidProposal();
        proposal.Claims =
        [
            new()
            {
                Id = "FACT",
                Kind = ClaimKind.RepositoryFact,
                Statement = "Items null is rejected.",
                Evidence =
                [
                    new()
                    {
                        File = "src/DnetList.razor",
                        Symbol = "requires the 'Items' parameters to be specified and non-null.",
                        Kind = "string_literal"
                    }
                ]
            }
        ];
        var source = "throw new InvalidOperationException($\"requires the '{ResolveParameterName()}' parameters to be specified and non-null.\");";

        var result = await new EvidenceValidator(new FakeReader(new Dictionary<string, string> { ["src/DnetList.razor"] = source }))
            .ValidateAsync(proposal, Snapshot(), CancellationToken.None);

        Assert.Equal(1, result.Unverified);
        Assert.Contains(result.Issues, issue => issue.Code == "EVIDENCE_SYMBOL_UNVERIFIABLE");
    }

    [Fact]
    public async Task One_verified_reference_keeps_a_claim_valid_when_a_redundant_reference_is_bad()
    {
        var proposal = ValidProposal();
        proposal.Claims =
        [
            new()
            {
                Id = "FACT",
                Kind = ClaimKind.RepositoryFact,
                Statement = "The marker exists.",
                Evidence =
                [
                    new() { File = "src/Real.cs", Symbol = "class Real", Kind = "type_declaration" },
                    new() { File = "src/Missing.cs", Symbol = "class Missing", Kind = "type_declaration" }
                ]
            }
        ];

        var result = await new EvidenceValidator(new FakeReader(new Dictionary<string, string> { ["src/Real.cs"] = "class Real {}" }))
            .ValidateAsync(proposal, Snapshot(), CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(1, result.Verified);
        Assert.Equal(0, result.Invalid);
        Assert.Contains(result.Issues, issue => issue.Code == "EVIDENCE_REFERENCE_IGNORED");
    }

    [Fact]
    public void Decision_linkage_and_step_completeness_are_enforced()
    {
        var proposal = ValidProposal();
        proposal.Decisions.Add(new() { Id = "D1", Statement = "decision", SupportedBy = ["UNKNOWN"] });
        proposal.ImplementationSteps[0].Tests.Clear();
        var result = new ArtifactValidator().ValidateProposal(proposal);
        Assert.Contains(result.Issues, x => x.Code == "DECISION_SUPPORT_REMOVED");
        Assert.Empty(proposal.Decisions[0].SupportedBy);
        Assert.Contains(result.Issues, x => x.Code == "STEP_TESTS_REQUIRED");
    }

    [Fact]
    public void Declared_decision_dependencies_are_valid_support_links()
    {
        var proposal = ValidProposal();
        proposal.Claims.Add(new() { Id = "C1", Kind = ClaimKind.ArchitecturalReasoning, Statement = "reason" });
        proposal.Decisions =
        [
            new() { Id = "D1", Statement = "canonical API", SupportedBy = ["C1"] },
            new() { Id = "D2", Statement = "use canonical API internally", SupportedBy = ["C1", "D1"] }
        ];

        var result = new ArtifactValidator().ValidateProposal(proposal);

        Assert.DoesNotContain(result.Issues, x => x.Code == "DECISION_CLAIM_MISSING");
    }

    [Fact]
    public void Safe_directory_style_target_paths_are_normalized_but_parent_traversal_still_blocks()
    {
        var proposal = ValidProposal();
        proposal.ImplementationSteps[0].Targets =
        [
            new() { Path = "tests\\ComponentTests/", Operation = TargetOperation.Modify }
        ];

        var normalized = new ArtifactValidator().ValidateProposal(proposal);

        Assert.True(normalized.IsValid);
        Assert.Equal("tests/ComponentTests", proposal.ImplementationSteps[0].Targets[0].Path);
        Assert.Contains(normalized.Issues, issue => issue.Code == "TARGET_PATH_NORMALIZED");

        proposal.ImplementationSteps[0].Targets[0].Path = "../outside/";
        var unsafeResult = new ArtifactValidator().ValidateProposal(proposal);
        Assert.Contains(unsafeResult.Issues, issue => issue.Code == "TARGET_PATH_INVALID");
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
        var incomplete = """{"summary":"x","claims":[],"decisions":[],"risks":[],"alternatives":[]}""";
        var parsed = new ArtifactParser().Parse<ProposalArtifact>(incomplete, Path.Combine(FindPlanAssets(), "Schemas", "proposal.schema.json"));
        Assert.Null(parsed.Artifact);
        Assert.Contains("implementationSteps", parsed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parser_defaults_nonessential_missing_collections_without_a_provider_retry()
    {
        var minimal = """{"summary":"x","implementationSteps":[]}""";

        var parsed = new ArtifactParser().Parse<ProposalArtifact>(minimal, Path.Combine(FindPlanAssets(), "Schemas", "proposal.schema.json"));

        Assert.NotNull(parsed.Artifact);
        Assert.Empty(parsed.Artifact!.Claims);
        Assert.Empty(parsed.Artifact.OpenQuestions);
    }

    [Fact]
    public void Parser_repairs_an_unambiguous_missing_array_comma_before_schema_validation()
    {
        var malformed = """
        ```json
        {
          "summary": "review",
          "proposalAliases": ["A1"],
          "claims": [],
          "incorrectAssumptions": [],
          "architecturalViolations": [],
          "missingInvariants": [],
          "complexityConcerns": [],
          "migrationRisks": [],
          "compatibilityProblems": [],
          "concurrencyConcerns": [],
          "securityConcerns": [],
          "missingTests": [],
          "rolloutRisks": [],
          "strongestIdeas": [
            "first idea"
            "second idea"
          ],
          "unresolvedDisagreements": []
        }
        ```
        """;

        var parsed = new ArtifactParser().Parse<ReviewArtifact>(malformed, Path.Combine(FindPlanAssets(), "Schemas", "review.schema.json"));

        Assert.NotNull(parsed.Artifact);
        Assert.Equal(["first idea", "second idea"], parsed.Artifact!.StrongestIdeas);
        Assert.NotNull(parsed.RepairedJson);
        Assert.Equal("inserted 1 missing JSON comma", parsed.RepairDescription);
    }

    [Fact]
    public void Parser_does_not_use_syntax_repair_to_bypass_the_schema()
    {
        var malformed = """{"summary":"x" "extra":"not allowed"}""";

        var parsed = new ArtifactParser().Parse<ProposalArtifact>(malformed, Path.Combine(FindPlanAssets(), "Schemas", "proposal.schema.json"));

        Assert.Null(parsed.Artifact);
        Assert.Null(parsed.RepairedJson);
    }

    [Fact]
    public void Final_plan_surfaces_assumptions_as_open_questions_locally()
    {
        var plan = new FinalPlanArtifact
        {
            Goal = "goal",
            Claims = [new() { Id = "A1", Kind = ClaimKind.Assumption, Statement = "Unknown deployment constraint" }],
            ImplementationSteps = [new() { Id = "S1", Targets = [new() { Path = "src/New.cs", Operation = TargetOperation.Create }], Changes = "change", Reason = "reason", Tests = ["test"] }]
        };
        var result = new ArtifactValidator().ValidateFinalPlan(plan);
        Assert.True(result.IsValid);
        Assert.Contains(result.Issues, x => x.Code == "ASSUMPTION_SURFACED");
        Assert.Contains("Unknown deployment constraint", plan.OpenQuestions);
    }

    [Fact]
    public void Final_plan_tracks_review_disagreements_by_id_while_allowing_paraphrases_and_grouping()
    {
        var plan = new FinalPlanArtifact
        {
            Goal = "goal",
            ImplementationSteps = [new() { Id = "S1", Targets = [new() { Path = "src/New.cs", Operation = TargetOperation.Create }], Changes = "change", Reason = "reason", Tests = ["test"] }],
            CouncilDisagreements =
            [
                new() { SourceIds = ["R1-D001", "R2-D001"], Summary = "One concise paraphrase covering both related concerns." }
            ]
        };

        var valid = new ArtifactValidator().ValidateFinalPlan(plan, ["R1-D001", "R2-D001"]);
        Assert.DoesNotContain(valid.Issues, x => x.Code.StartsWith("DISAGREEMENT_", StringComparison.Ordinal));

        plan.CouncilDisagreements[0].SourceIds = ["R1-D001", "UNKNOWN", "R1-D001"];
        var invalid = new ArtifactValidator().ValidateFinalPlan(plan, ["R1-D001", "R2-D001"]);
        Assert.Contains(invalid.Issues, x => x.Code == "DISAGREEMENT_DUPLICATE_REMOVED");
        Assert.Contains(invalid.Issues, x => x.Code == "DISAGREEMENT_SOURCE_REMOVED");
        Assert.Contains(invalid.Issues, x => x.Code == "DISAGREEMENT_DROPPED" && x.Message.Contains("R2-D001", StringComparison.Ordinal));
    }

    private static ProposalArtifact ValidProposal() => new()
    {
        Summary = "summary",
        ImplementationSteps = [new() { Id = "S1", Targets = [new() { Path = "src/New.cs", Operation = TargetOperation.Create }], Changes = "change", Reason = "reason", Tests = ["test"] }]
    };

    private static RepositorySnapshot Snapshot() => new("key", ".", "a", "b", "refs/conclave/runs/key", SnapshotMode.Head, false, false);

    private static string FindPlanAssets()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var planAssets = Path.Combine(directory.FullName, "src", "Modules", "Planning", "Features", "Plan");
            if (Directory.Exists(Path.Combine(planAssets, "Schemas"))) return planAssets;
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
