using System.Text.Json;
using Conclave.Core;
using Conclave.Infrastructure;

namespace Conclave.Core.Tests;

public sealed class CoreContractTests
{
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
        configuration.ProviderBudget.MaxCalls = 1;
        var budget = new BudgetManager(configuration);
        var request = new ModelRequest("r", ConclaveStage.Proposal, "p", ".", "schema", new("codex", "m"));
        Assert.True(budget.CanStart(request).Allowed);
        budget.Record(new(request.Participant, request.Stage, true, ProviderFailureKind.None, "{}", new(10, null, 5), TimeSpan.FromSeconds(1), 0, null));
        var denied = budget.CanStart(request);
        Assert.False(denied.Allowed);
        Assert.Equal(ConclaveExitCode.ProviderBudgetExceeded, denied.ExitCode);
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
}
