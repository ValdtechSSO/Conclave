using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Conclave.Planning;

namespace Conclave.Planning.Features.Plan;

public sealed record ArtifactParseResult<T>(T? Artifact, string? Error, string? RepairedJson = null, string? RepairDescription = null) where T : class;

public sealed class ArtifactParser
{
    public ArtifactParseResult<T> Parse<T>(string? content, string? schemaPath = null) where T : class
    {
        if (string.IsNullOrWhiteSpace(content)) return new(null, "Provider returned no structured output.");
        string? schemaError = null;
        var candidates = Candidates(content).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var candidate in candidates)
        {
            var parsed = ParseCandidate<T>(candidate, schemaPath);
            if (parsed.Artifact is not null) return parsed;
            schemaError ??= parsed.Error;
        }

        foreach (var candidate in candidates)
        {
            if (!TryInsertMissingLineCommas(candidate, out var repaired, out var insertions)) continue;
            var parsed = ParseCandidate<T>(repaired, schemaPath);
            if (parsed.Artifact is not null)
                return new(parsed.Artifact, null, repaired, $"inserted {insertions} missing JSON comma{(insertions == 1 ? "" : "s")}");
            schemaError ??= parsed.Error;
        }

        return new(null, schemaError ?? "Provider output does not contain a valid artifact matching the required JSON contract.");
    }

    private static ArtifactParseResult<T> ParseCandidate<T>(string candidate, string? schemaPath) where T : class
    {
        try
        {
            if (schemaPath is not null)
            {
                var schemaValidation = JsonSchemaSubsetValidator.Validate(candidate, schemaPath);
                if (!schemaValidation.Valid) return new(null, schemaValidation.Error);
            }
            var result = JsonSerializer.Deserialize<T>(candidate, ConclaveJson.Options);
            return result is null ? new(null, "JSON deserialized to null.") : new(result, null);
        }
        catch (JsonException exception) { return new(null, exception.Message); }
    }

    private static bool TryInsertMissingLineCommas(string candidate, out string repaired, out int insertions)
    {
        var lines = candidate.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        insertions = 0;
        for (var index = 0; index + 1 < lines.Length; index++)
        {
            var current = lines[index].TrimEnd();
            var next = lines[index + 1].TrimStart();
            if (!EndsWithCompleteJsonValue(current) || !StartsWithJsonValue(next)) continue;
            lines[index] = current + ",";
            insertions++;
        }
        repaired = string.Join('\n', lines);
        return insertions > 0;
    }

    private static bool EndsWithCompleteJsonValue(string line)
    {
        if (line.Length == 0) return false;
        var last = line[^1];
        if (last == '"')
        {
            var backslashes = 0;
            for (var index = line.Length - 2; index >= 0 && line[index] == '\\'; index--) backslashes++;
            return backslashes % 2 == 0;
        }
        if (last is '}' or ']' || char.IsDigit(last)) return true;
        return line.EndsWith("true", StringComparison.Ordinal) || line.EndsWith("false", StringComparison.Ordinal) || line.EndsWith("null", StringComparison.Ordinal);
    }

    private static bool StartsWithJsonValue(string line)
    {
        if (line.Length == 0) return false;
        var first = line[0];
        return first is '"' or '{' or '[' or '-' || char.IsDigit(first) ||
            line.StartsWith("true", StringComparison.Ordinal) || line.StartsWith("false", StringComparison.Ordinal) || line.StartsWith("null", StringComparison.Ordinal);
    }

    private static IEnumerable<string> Candidates(string content)
    {
        yield return content.Trim();
        var fenced = content.Trim();
        if (fenced.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLine = fenced.IndexOf('\n');
            var lastFence = fenced.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLine >= 0 && lastFence > firstLine) yield return fenced[(firstLine + 1)..lastFence].Trim();
        }
        foreach (var line in content.Split('\n').Reverse())
            if (line.TrimStart().StartsWith('{')) yield return line.Trim();
        var first = content.IndexOf('{');
        var last = content.LastIndexOf('}');
        if (first >= 0 && last > first) yield return content[first..(last + 1)];
    }
}

internal static class JsonSchemaSubsetValidator
{
    public static (bool Valid, string? Error) Validate(string json, string schemaPath)
    {
        try
        {
            using var instance = JsonDocument.Parse(json);
            using var schema = JsonDocument.Parse(File.ReadAllText(schemaPath));
            var error = ValidateNode(instance.RootElement, schema.RootElement, schema.RootElement, "$", new HashSet<string>(StringComparer.Ordinal));
            return (error is null, error);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return (false, exception.Message);
        }
    }

    private static string? ValidateNode(JsonElement instance, JsonElement schema, JsonElement root, string path, HashSet<string> references)
    {
        if (schema.TryGetProperty("$ref", out var referenceElement))
        {
            var reference = referenceElement.GetString() ?? "";
            if (!reference.StartsWith("#/$defs/", StringComparison.Ordinal)) return $"Unsupported schema reference '{reference}' at {path}.";
            if (!references.Add(reference)) return $"Recursive schema reference '{reference}' at {path}.";
            var name = reference["#/$defs/".Length..];
            if (!root.TryGetProperty("$defs", out var definitions) || !definitions.TryGetProperty(name, out var resolved)) return $"Unresolved schema reference '{reference}'.";
            var resolvedError = ValidateNode(instance, resolved, root, path, references);
            references.Remove(reference);
            return resolvedError;
        }

        if (schema.TryGetProperty("type", out var type) && !MatchesType(instance, type)) return $"JSON value at {path} has the wrong type.";
        if (schema.TryGetProperty("enum", out var enumValues) && !enumValues.EnumerateArray().Any(value => JsonElement.DeepEquals(value, instance))) return $"JSON value at {path} is outside the allowed enum.";
        if (instance.ValueKind == JsonValueKind.String && schema.TryGetProperty("minLength", out var minLength) && (instance.GetString()?.Length ?? 0) < minLength.GetInt32()) return $"String at {path} is too short.";
        if (instance.ValueKind == JsonValueKind.Array)
        {
            var values = instance.EnumerateArray().ToArray();
            if (schema.TryGetProperty("minItems", out var minItems) && values.Length < minItems.GetInt32()) return $"Array at {path} has too few items.";
            if (schema.TryGetProperty("items", out var itemSchema))
                for (var index = 0; index < values.Length; index++)
                {
                    var error = ValidateNode(values[index], itemSchema, root, $"{path}[{index}]", references);
                    if (error is not null) return error;
                }
        }
        if (instance.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("required", out var required))
                foreach (var name in required.EnumerateArray().Select(x => x.GetString()!))
                    if (!instance.TryGetProperty(name, out _)) return $"Required property '{name}' is missing at {path}.";
            var hasProperties = schema.TryGetProperty("properties", out var properties);
            if (schema.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.False && hasProperties)
                foreach (var property in instance.EnumerateObject())
                    if (!properties.TryGetProperty(property.Name, out _)) return $"Additional property '{property.Name}' is not allowed at {path}.";
            if (hasProperties)
                foreach (var property in instance.EnumerateObject())
                    if (properties.TryGetProperty(property.Name, out var propertySchema))
                    {
                        var error = ValidateNode(property.Value, propertySchema, root, $"{path}.{property.Name}", references);
                        if (error is not null) return error;
                    }
        }
        return null;
    }

    private static bool MatchesType(JsonElement instance, JsonElement type)
    {
        if (type.ValueKind == JsonValueKind.Array) return type.EnumerateArray().Any(value => MatchesType(instance, value));
        return type.GetString() switch
        {
            "object" => instance.ValueKind == JsonValueKind.Object,
            "array" => instance.ValueKind == JsonValueKind.Array,
            "string" => instance.ValueKind == JsonValueKind.String,
            "number" => instance.ValueKind == JsonValueKind.Number,
            "integer" => instance.ValueKind == JsonValueKind.Number && instance.TryGetInt64(out _),
            "boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => instance.ValueKind == JsonValueKind.Null,
            _ => true
        };
    }
}

public sealed class ArtifactValidator : IArtifactValidator
{
    public ValidationResult ValidateProposal(ProposalArtifact artifact)
    {
        var result = new ValidationResult();
        Required(result, artifact.Summary, "proposal.summary");
        ValidateClaims(result, artifact.Claims);
        ValidateDecisions(result, artifact.Decisions, artifact.Claims);
        ValidateSteps(result, artifact.ImplementationSteps);
        return result;
    }

    public ValidationResult ValidateReview(ReviewArtifact artifact)
    {
        var result = new ValidationResult();
        Required(result, artifact.Summary, "review.summary");
        if (artifact.ProposalAliases.Count == 0) Invalid(result, "REVIEW_PROPOSALS_REQUIRED", "Review must identify at least one reviewed proposal.", "review.proposalAliases");
        ValidateClaims(result, artifact.Claims);
        return result;
    }

    public ValidationResult ValidateFinalPlan(FinalPlanArtifact artifact, IReadOnlyCollection<string>? requiredDisagreementIds = null)
    {
        var result = new ValidationResult();
        Required(result, artifact.Goal, "finalPlan.goal");
        ValidateClaims(result, artifact.Claims);
        ValidateDecisions(result, artifact.ArchitecturalDecisions, artifact.Claims);
        ValidateSteps(result, artifact.ImplementationSteps);
        if (artifact.ImplementationSteps.Count == 0) Invalid(result, "PLAN_STEPS_REQUIRED", "Final plan requires at least one implementation step.", "finalPlan.implementationSteps");
        foreach (var assumption in artifact.Claims.Where(x => x.Kind == ClaimKind.Assumption))
            if (!artifact.OpenQuestions.Contains(assumption.Statement, StringComparer.Ordinal))
            {
                artifact.OpenQuestions.Add(assumption.Statement);
                Warning(result, "ASSUMPTION_SURFACED", $"Assumption '{assumption.Id}' was copied to open questions locally.", $"claim[{assumption.Id}]");
            }
        ValidateDisagreements(result, artifact.CouncilDisagreements, requiredDisagreementIds);
        return result;
    }

    private static void ValidateDisagreements(ValidationResult result, List<CouncilDisagreement> disagreements, IReadOnlyCollection<string>? requiredIds)
    {
        var required = requiredIds?.ToHashSet(StringComparer.Ordinal);
        var referencedIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = disagreements.Count - 1; index >= 0; index--)
        {
            var disagreement = disagreements[index];
            Required(result, disagreement.Summary, $"finalPlan.councilDisagreements[{index}].summary");
            var normalized = new List<string>();
            foreach (var sourceId in disagreement.SourceIds.Select(value => value.Trim()).Where(value => value.Length > 0))
            {
                if (required is not null && !required.Contains(sourceId))
                {
                    Warning(result, "DISAGREEMENT_SOURCE_REMOVED", $"Unknown disagreement catalog ID '{sourceId}' was removed locally.", $"finalPlan.councilDisagreements[{index}].sourceIds");
                    continue;
                }
                if (!referencedIds.Add(sourceId))
                {
                    Warning(result, "DISAGREEMENT_DUPLICATE_REMOVED", $"Duplicate disagreement catalog ID '{sourceId}' was removed locally.", $"finalPlan.councilDisagreements[{index}].sourceIds");
                    continue;
                }
                normalized.Add(sourceId);
            }
            disagreement.SourceIds = normalized;
            if (normalized.Count == 0)
            {
                disagreements.RemoveAt(index);
                Warning(result, "DISAGREEMENT_ENTRY_REMOVED", "A disagreement entry without usable catalog IDs was removed locally.", $"finalPlan.councilDisagreements[{index}]");
            }
        }

        if (required is null) return;
        foreach (var missing in required.Except(referencedIds, StringComparer.Ordinal))
            Invalid(result, "DISAGREEMENT_DROPPED", $"Review disagreement '{missing}' was not preserved by ID.", "finalPlan.councilDisagreements");
    }

    private static void ValidateClaims(ValidationResult result, List<Claim> claims)
    {
        Unique(result, claims.Select(x => x.Id), "CLAIM_ID_DUPLICATE", "claims");
        foreach (var claim in claims)
        {
            Required(result, claim.Id, "claim.id");
            Required(result, claim.Statement, $"claim[{claim.Id}].statement");
            if (claim.Kind == ClaimKind.RepositoryFact && claim.Evidence.Count == 0)
                Invalid(result, "REPOSITORY_FACT_EVIDENCE_REQUIRED", $"Repository fact '{claim.Id}' requires evidence.", $"claim[{claim.Id}].evidence");
        }
    }

    private static void ValidateDecisions(ValidationResult result, List<ArchitecturalDecision> decisions, List<Claim> claims)
    {
        Unique(result, decisions.Select(x => x.Id), "DECISION_ID_DUPLICATE", "decisions");
        var claimIds = claims.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var decisionIds = decisions.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var decision in decisions)
        {
            Required(result, decision.Id, "decision.id");
            Required(result, decision.Statement, $"decision[{decision.Id}].statement");
            foreach (var supportId in decision.SupportedBy)
                if (!claimIds.Contains(supportId) && !decisionIds.Contains(supportId))
                    Warning(result, "DECISION_SUPPORT_REMOVED", $"Decision '{decision.Id}' referenced unknown claim or decision '{supportId}'; the reference was removed locally.", $"decision[{decision.Id}].supportedBy");
            decision.SupportedBy = decision.SupportedBy
                .Where(supportId => claimIds.Contains(supportId) || decisionIds.Contains(supportId))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
    }

    private static void ValidateSteps(ValidationResult result, List<ImplementationStep> steps)
    {
        Unique(result, steps.Select(x => x.Id), "STEP_ID_DUPLICATE", "implementationSteps");
        foreach (var step in steps)
        {
            Required(result, step.Id, "step.id");
            Required(result, step.Changes, $"step[{step.Id}].changes");
            Required(result, step.Reason, $"step[{step.Id}].reason");
            if (step.Targets.Count == 0) Invalid(result, "STEP_TARGETS_REQUIRED", $"Step '{step.Id}' requires targets.", $"step[{step.Id}].targets");
            if (step.Tests.RemoveAll(string.IsNullOrWhiteSpace) > 0)
                Warning(result, "EMPTY_TEST_REMOVED", $"Blank test entries were removed locally from step '{step.Id}'.", $"step[{step.Id}].tests");
            if (step.Tests.Count == 0) Invalid(result, "STEP_TESTS_REQUIRED", $"Step '{step.Id}' requires non-empty tests.", $"step[{step.Id}].tests");
            var targetOperations = new Dictionary<string, TargetOperation>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in step.Targets)
            {
                var normalizedPath = NormalizePlanPath(target.Path);
                if (!string.Equals(target.Path, normalizedPath, StringComparison.Ordinal))
                {
                    Warning(result, "TARGET_PATH_NORMALIZED", $"Target path '{target.Path}' was normalized locally to '{normalizedPath}'.", $"step[{step.Id}].targets");
                    target.Path = normalizedPath;
                }
                if (!RepositoryPath.IsSafeRelative(target.Path))
                {
                    Invalid(result, "TARGET_PATH_INVALID", $"Target path '{target.Path}' must be repository-relative and cannot traverse parents.", $"step[{step.Id}].targets");
                    continue;
                }
                if (targetOperations.TryGetValue(target.Path, out var existing) && existing != target.Operation)
                    Invalid(result, "TARGET_OPERATION_CONFLICT", $"Target '{target.Path}' has conflicting operations.", $"step[{step.Id}].targets");
                else targetOperations[target.Path] = target.Operation;
                if (target.Operation is TargetOperation.Rename or TargetOperation.Move)
                {
                    var normalizedDestination = NormalizePlanPath(target.Destination ?? "");
                    if (!string.Equals(target.Destination, normalizedDestination, StringComparison.Ordinal))
                    {
                        Warning(result, "TARGET_DESTINATION_NORMALIZED", $"Target destination '{target.Destination}' was normalized locally to '{normalizedDestination}'.", $"step[{step.Id}].targets");
                        target.Destination = normalizedDestination;
                    }
                    if (!RepositoryPath.IsSafeRelative(normalizedDestination))
                        Invalid(result, "TARGET_DESTINATION_INVALID", $"Target '{target.Path}' requires a safe destination.", $"step[{step.Id}].targets");
                }
            }
        }
    }

    private static string NormalizePlanPath(string path) => path.Trim().Replace('\\', '/').TrimEnd('/');

    private static void Required(ValidationResult result, string value, string location)
    {
        if (string.IsNullOrWhiteSpace(value)) Invalid(result, "REQUIRED_VALUE_MISSING", $"Required value is missing at {location}.", location);
    }

    private static void Unique(ValidationResult result, IEnumerable<string> values, string code, string location)
    {
        foreach (var duplicate in values.Where(x => !string.IsNullOrWhiteSpace(x)).GroupBy(x => x, StringComparer.Ordinal).Where(x => x.Count() > 1).Select(x => x.Key))
            Invalid(result, code, $"Identifier '{duplicate}' is duplicated.", location);
    }

    private static void Invalid(ValidationResult result, string code, string message, string location) => result.Issues.Add(new(code, message, location, EvidenceStatus.Invalid));
    private static void Warning(ValidationResult result, string code, string message, string location) => result.Issues.Add(new(code, message, location, EvidenceStatus.NotDeterministicallyVerifiable));
}

public sealed class EvidenceValidator(IRepositoryContentReader contentReader) : IEvidenceValidator
{
    public async Task<ValidationResult> ValidateAsync(IConclaveArtifact artifact, RepositorySnapshot snapshot, CancellationToken cancellationToken)
    {
        var result = new ValidationResult();
        foreach (var claim in artifact.Claims)
        {
            if (claim.Kind != ClaimKind.RepositoryFact) continue;
            result.TotalRepositoryClaims++;
            if (claim.Evidence.Count == 0)
            {
                result.Invalid++;
                result.Issues.Add(new("EVIDENCE_REQUIRED", $"Repository fact '{claim.Id}' has no evidence.", claim.Id, EvidenceStatus.Invalid));
                continue;
            }

            var unsafeReference = false;
            var verifiedReference = false;
            var existingFile = false;
            var deferredIssues = new List<(string Code, string Message)>();
            foreach (var evidence in claim.Evidence)
            {
                if (!RepositoryPath.IsSafeRelative(evidence.File) || evidence.File.StartsWith(".conclave-input/", StringComparison.Ordinal))
                {
                    unsafeReference = true;
                    result.Issues.Add(new("EVIDENCE_PATH_INVALID", $"Evidence path '{evidence.File}' is not admissible.", claim.Id, EvidenceStatus.Invalid));
                    continue;
                }
                var content = await contentReader.ReadTextAsync(snapshot, evidence.File, cancellationToken);
                if (!content.Exists)
                {
                    deferredIssues.Add(("EVIDENCE_FILE_MISSING", $"Evidence file '{evidence.File}' does not exist in snapshot {snapshot.SnapshotSha}."));
                    continue;
                }
                existingFile = true;
                if (!string.IsNullOrWhiteSpace(evidence.Symbol) &&
                    !EvidenceSymbolMatcher.Contains(content.Content ?? "", evidence.Symbol, evidence.Kind, evidence.File))
                {
                    deferredIssues.Add(("EVIDENCE_SYMBOL_UNVERIFIABLE", $"Symbol '{evidence.Symbol}' was not found exactly in '{evidence.File}'."));
                    continue;
                }
                verifiedReference = true;
            }

            if (unsafeReference)
            {
                result.Invalid++;
                foreach (var issue in deferredIssues)
                    result.Issues.Add(new(issue.Code, issue.Message, claim.Id, EvidenceStatus.NotDeterministicallyVerifiable));
            }
            else if (verifiedReference)
            {
                result.Verified++;
                foreach (var issue in deferredIssues)
                    result.Issues.Add(new("EVIDENCE_REFERENCE_IGNORED", issue.Message + " Another evidence reference verified the claim.", claim.Id, EvidenceStatus.NotDeterministicallyVerifiable));
            }
            else if (!existingFile)
            {
                result.Invalid++;
                foreach (var issue in deferredIssues)
                    result.Issues.Add(new(issue.Code, issue.Message, claim.Id, EvidenceStatus.Invalid));
            }
            else
            {
                result.Unverified++;
                foreach (var issue in deferredIssues)
                    result.Issues.Add(new(issue.Code, issue.Message, claim.Id, EvidenceStatus.NotDeterministicallyVerifiable));
            }
        }
        return result;
    }
}

internal static partial class EvidenceSymbolMatcher
{
    public static bool Contains(string content, string symbol, string kind, string file)
    {
        if (content.Contains(symbol, StringComparison.Ordinal)) return true;
        if (!string.Equals(kind, "string_literal", StringComparison.OrdinalIgnoreCase) || !IsCSharpSource(file)) return false;

        var expanded = CSharpNameofInterpolation().Replace(content, static match =>
        {
            var identifiers = CSharpIdentifier().Matches(match.Groups["expression"].Value);
            return identifiers.Count == 0 ? match.Value : identifiers[^1].Value.TrimStart('@');
        });
        return expanded.Contains(symbol, StringComparison.Ordinal);
    }

    private static bool IsCSharpSource(string file) =>
        file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
        file.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) ||
        file.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"(?<!\{)\{\s*nameof\s*\(\s*(?<expression>(?:global::)?@?[A-Za-z_][A-Za-z0-9_]*(?:\s*\.\s*@?[A-Za-z_][A-Za-z0-9_]*)*)\s*\)\s*\}(?!\})", RegexOptions.CultureInvariant)]
    private static partial Regex CSharpNameofInterpolation();

    [GeneratedRegex(@"@?[A-Za-z_][A-Za-z0-9_]*", RegexOptions.CultureInvariant)]
    private static partial Regex CSharpIdentifier();
}

public static class ValidationResults
{
    public static ValidationResult Merge(params ValidationResult[] values)
    {
        var result = new ValidationResult();
        foreach (var value in values)
        {
            result.TotalRepositoryClaims += value.TotalRepositoryClaims;
            result.Verified += value.Verified;
            result.Unverified += value.Unverified;
            result.Invalid += value.Invalid;
            result.Issues.AddRange(value.Issues);
        }
        return result;
    }
}

public sealed class MarkdownPlanRenderer : IPlanRenderer
{
    public string Render(FinalPlanArtifact plan, RunResult run)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Implementation Plan").AppendLine();
        Section(builder, "1. Goal", [plan.Goal]);
        Section(builder, "2. Relevant Existing Architecture", plan.RelevantArchitecture);
        Section(builder, "3. Invariants", plan.Invariants);
        builder.AppendLine("## 4. Architectural Decisions").AppendLine();
        foreach (var decision in plan.ArchitecturalDecisions)
            builder.AppendLine($"- **{decision.Id}:** {decision.Statement} (supported by: {string.Join(", ", decision.SupportedBy.DefaultIfEmpty("none"))})");
        Empty(builder, plan.ArchitecturalDecisions.Count);
        Section(builder, "5. Domain Changes", plan.DomainChanges);
        Section(builder, "6. Data Model / Persistence Changes", plan.PersistenceChanges);
        Section(builder, "7. API Changes", plan.ApiChanges);
        Section(builder, "8. Components Affected", plan.AffectedComponents);
        builder.AppendLine("## 9. Detailed Implementation Sequence").AppendLine();
        foreach (var (step, index) in plan.ImplementationSteps.Select((value, index) => (value, index)))
        {
            builder.AppendLine($"### Step {index + 1}: {step.Id}").AppendLine();
            builder.AppendLine("Targets/components:");
            foreach (var target in step.Targets) builder.AppendLine($"- `{target.Path}` — {target.Operation.ToString().ToLowerInvariant()}{(target.Destination is null ? "" : $" → `{target.Destination}`")}");
            builder.AppendLine().AppendLine($"Changes: {step.Changes}").AppendLine().AppendLine($"Reason: {step.Reason}").AppendLine().AppendLine("Tests:");
            foreach (var test in step.Tests) builder.AppendLine($"- {test}");
            builder.AppendLine();
        }
        Section(builder, "10. Migration / Backward Compatibility", plan.Migration);
        Section(builder, "11. Testing Strategy", plan.Testing);
        Section(builder, "12. Observability", plan.Observability);
        Section(builder, "13. Security Considerations", plan.Security);
        Section(builder, "14. Risks", plan.Risks);
        Section(builder, "15. Alternatives Rejected", plan.RejectedAlternatives);
        builder.AppendLine("## 16. Conclave Disagreements").AppendLine();
        foreach (var disagreement in plan.CouncilDisagreements)
            builder.AppendLine($"- {disagreement.Summary} (sources: {string.Join(", ", disagreement.SourceIds)})");
        Empty(builder, plan.CouncilDisagreements.Count);
        Section(builder, "17. Open Questions", plan.OpenQuestions);
        builder.AppendLine("## 18. Repository Evidence").AppendLine();
        foreach (var claim in plan.Claims)
        {
            builder.AppendLine($"- **{claim.Id}** ({claim.Kind.ToString().ToLowerInvariant()}): {claim.Statement}");
            foreach (var evidence in claim.Evidence) builder.AppendLine($"  - `{evidence.File}`{(evidence.Symbol is null ? "" : $" — `{evidence.Symbol}`")}");
        }
        Empty(builder, plan.Claims.Count);
        builder.AppendLine("## 19. Conclave Execution Metadata").AppendLine();
        builder.AppendLine($"- Run ID: `{run.RunId}`");
        builder.AppendLine($"- Snapshot SHA: `{run.SnapshotSha}`");
        builder.AppendLine($"- Participating providers: {string.Join(", ", run.Providers)}");
        builder.AppendLine($"- Missing providers: {string.Join(", ", run.MissingProviders.DefaultIfEmpty("none"))}");
        builder.AppendLine($"- Evidence warnings: {run.Warnings.Count}");
        builder.AppendLine($"- Known token usage: {run.Usage.KnownTokens}");
        builder.AppendLine($"- Duration: {((run.CompletedAt ?? DateTimeOffset.UtcNow) - run.StartedAt).TotalSeconds:F1}s");
        return builder.ToString();
    }

    private static void Section(StringBuilder builder, string heading, IReadOnlyCollection<string> values)
    {
        builder.AppendLine($"## {heading}").AppendLine();
        foreach (var value in values) builder.AppendLine($"- {value}");
        Empty(builder, values.Count);
    }

    private static void Empty(StringBuilder builder, int count)
    {
        if (count == 0) builder.AppendLine("- None.");
        builder.AppendLine();
    }
}
