using System.Text.Json;
using Conclave.Planning;
using Conclave.Planning.Features.CreatePlan;
using Conclave.Planning.Infrastructure;

namespace Conclave.Planning.UnitTests;

public sealed class CoreContractTests
{
    [Fact]
    public void Token_budgets_are_provider_specific_and_configuration_can_override_them()
    {
        var defaults = ConfigurationLoader.Defaults();
        Assert.Equal(1_000_000, defaults.ProviderBudget.MaxTokens);
        Assert.Equal(1_000_000, defaults.Providers["codex"].Budget!.MaxTokens);
        Assert.Equal(4_000_000, defaults.Providers["deepseek"].Budget!.MaxTokens);

        var repository = Path.Combine(Path.GetTempPath(), "conclave-budget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repository);
        try
        {
            File.WriteAllText(Path.Combine(repository, ".conclave.yaml"), "providers:\n  codex:\n    budget:\n      maxTokens: 456789\n  deepseek:\n    timeoutSeconds: 720\n    budget:\n      maxTokens: 7654321\n");
            var configured = new ConfigurationLoader().Load(repository);

            Assert.Equal(456_789, configured.Providers["codex"].Budget!.MaxTokens);
            Assert.Equal(7_654_321, configured.Providers["deepseek"].Budget!.MaxTokens);
            Assert.Equal(720, configured.Providers["deepseek"].TimeoutSeconds);
        }
        finally { Directory.Delete(repository, recursive: true); }
    }

    [Fact]
    public void Provider_token_usage_is_isolated_between_providers()
    {
        var configuration = ConfigurationLoader.Defaults();
        configuration.Providers["codex"].Budget = new() { MaxTokens = 10, MaxDurationMinutes = 5, MaxCalls = 3, MaxCostUsd = 1 };
        configuration.Providers["deepseek"].Budget = new() { MaxTokens = 100, MaxDurationMinutes = 5, MaxCalls = 3, MaxCostUsd = 1 };
        var budget = new BudgetManager(configuration);
        var codex = new ModelRequest("r", ConclaveStage.Proposal, "p", ".", "schema", new("codex", "m"));
        var deepseek = new ModelRequest("r", ConclaveStage.Proposal, "p", ".", "schema", new("deepseek", "m"));

        budget.Record(new(codex.Participant, codex.Stage, true, ProviderFailureKind.None, "{}", new(8, null, 2), TimeSpan.FromSeconds(1), 0, null));

        Assert.Equal(ConclaveExitCode.ProviderBudgetExceeded, budget.CanStart(codex).ExitCode);
        Assert.True(budget.CanStart(deepseek).Allowed);
    }

    [Fact]
    public void Json_contract_uses_snake_case_enums_and_rejects_unknown_members()
    {
        var claim = new Claim { Id = "C1", Kind = ClaimKind.RepositoryFact, Statement = "fact" };
        var json = JsonSerializer.Serialize(claim, ConclaveJson.Options);
        Assert.Contains("repository_fact", json, StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Claim>("""{"id":"C1","kind":"repository_fact","statement":"fact","evidence":[],"surprise":true}""", ConclaveJson.Options));
    }

    [Fact]
    public void Budget_prevents_calls_after_provider_limit()
    {
        var configuration = ConfigurationLoader.Defaults();
        configuration.Providers["codex"].Budget!.MaxCalls = 1;
        var budget = new BudgetManager(configuration);
        var request = new ModelRequest("r", ConclaveStage.Proposal, "p", ".", "schema", new("codex", "m"));
        Assert.True(budget.CanStart(request).Allowed);
        budget.Record(new(request.Participant, request.Stage, true, ProviderFailureKind.None, "{}", new(10, null, 5), TimeSpan.FromSeconds(1), 0, null));
        var denied = budget.CanStart(request);
        Assert.False(denied.Allowed);
        Assert.Equal(ConclaveExitCode.ProviderBudgetExceeded, denied.ExitCode);
    }

    [Fact]
    public void Budget_prevents_calls_after_reported_usd_cost_limit()
    {
        var configuration = ConfigurationLoader.Defaults();
        configuration.Providers["claude"].Budget!.MaxCostUsd = 0.20m;
        var budget = new BudgetManager(configuration);
        var request = new ModelRequest("r", ConclaveStage.Proposal, "p", ".", "schema", new("claude", "sonnet"));

        budget.Record(new(request.Participant, request.Stage, true, ProviderFailureKind.None, "{}", new(Cost: 0.20m, Currency: "USD"), TimeSpan.FromSeconds(1), 0, null));

        Assert.Equal(ConclaveExitCode.ProviderBudgetExceeded, budget.CanStart(request).ExitCode);
    }

    [Fact]
    public void Run_ids_are_sanitized_for_storage()
    {
        Assert.Equal("feature---1", FileRunStore.SanitizeRunId("feature / 1"));
        Assert.Throws<ArgumentException>(() => FileRunStore.SanitizeRunId("   "));
    }

    [Fact]
    public void Repository_secret_file_is_loaded_without_entering_serialized_configuration()
    {
        var repository = Path.Combine(Path.GetTempPath(), "conclave-secrets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repository);
        try
        {
            File.WriteAllText(Path.Combine(repository, ".conclave.secrets.env"), "ANTHROPIC_API_KEY=anthropic-test-secret\nDEEPSEEK_API_KEY='deepseek-test-secret'\nOPENAI_API_KEY=openai-test-secret\n");
            var configuration = new ConfigurationLoader().Load(repository);
            Assert.Equal("anthropic-test-secret", configuration.Providers["claude"].CredentialValue);
            Assert.Equal("deepseek-test-secret", configuration.Providers["deepseek"].CredentialValue);
            Assert.Equal("openai-test-secret", configuration.Providers["codex"].CredentialValue);
            var serialized = JsonSerializer.Serialize(configuration.Providers["claude"], ConclaveJson.Options);
            Assert.DoesNotContain("anthropic-test-secret", serialized, StringComparison.Ordinal);
        }
        finally { Directory.Delete(repository, recursive: true); }
    }

    [Fact]
    public void Secret_file_rejects_arbitrary_environment_variables()
    {
        var repository = Path.Combine(Path.GetTempPath(), "conclave-secrets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repository);
        try
        {
            File.WriteAllText(Path.Combine(repository, ".conclave.secrets.env"), "PATH=/untrusted\n");
            Assert.Throws<InvalidDataException>(() => new ConfigurationLoader().Load(repository));
        }
        finally { Directory.Delete(repository, recursive: true); }
    }

    [Fact]
    public void Default_provider_schema_dialects_match_their_cli_constraints()
    {
        var configuration = ConfigurationLoader.Defaults();

        Assert.Equal(JsonSchemaDialect.DraftAgnostic, configuration.Providers["claude"].JsonSchemaDialect);
        Assert.Equal(JsonSchemaDialect.OpenAiStrict, configuration.Providers["codex"].JsonSchemaDialect);
        Assert.Equal(JsonSchemaDialect.OpenAiStrict, configuration.Providers["deepseek"].JsonSchemaDialect);
        Assert.Equal("sonnet", configuration.Providers["claude"].Proposal.Model);
        Assert.Equal("gpt-5.6-sol", configuration.Providers["codex"].Proposal.Model);
        Assert.Equal("deepseek-v4-flash", configuration.Providers["deepseek"].Proposal.Model);
        Assert.Equal(360, configuration.Providers["claude"].TimeoutSeconds);
        Assert.Equal(360, configuration.Providers["codex"].TimeoutSeconds);
        Assert.Equal(600, configuration.Providers["deepseek"].TimeoutSeconds);
        Assert.Equal("codex", configuration.Providers["deepseek"].Command);
        Assert.Equal(PromptTransport.Stdin, configuration.Providers["deepseek"].PromptTransport);
        Assert.True(configuration.Providers["deepseek"].CredentialRequired);
        Assert.Contains("model_provider=\"deepseek\"", configuration.Providers["deepseek"].Proposal.Arguments);
        Assert.Contains("model_providers.deepseek.wire_api=\"responses\"", configuration.Providers["deepseek"].Proposal.Arguments);
        Assert.Contains("model_providers.deepseek.env_key=\"DEEPSEEK_API_KEY\"", configuration.Providers["deepseek"].Proposal.Arguments);
        Assert.Contains("model_providers.deepseek.request_max_retries=0", configuration.Providers["deepseek"].Proposal.Arguments);
        Assert.Contains("--output-schema", configuration.Providers["deepseek"].Proposal.Arguments);
        Assert.Contains("read-only", configuration.Providers["deepseek"].Proposal.Arguments);
        Assert.Contains("project_doc_max_bytes=0", configuration.Providers["deepseek"].Proposal.Arguments);
        Assert.Contains("Read", configuration.Providers["claude"].Proposal.Arguments);
        Assert.Contains("--max-budget-usd", configuration.Providers["claude"].Proposal.Arguments);
        Assert.Contains("stream-json", configuration.Providers["claude"].Proposal.Arguments);
    }
}
