using System.Text.Json;
using Conclave.Core;
using Conclave.Infrastructure;
using Conclave.Providers;

namespace Conclave.Providers.Tests;

public sealed class ProviderTests : IDisposable
{
    private readonly string _schemaRoot = Path.Combine(Path.GetTempPath(), "conclave-provider-schema-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_schemaRoot)) Directory.Delete(_schemaRoot, recursive: true);
    }

    [Fact]
    public async Task Adapter_passes_stage_model_and_collects_output()
    {
        var configuration = Config("printf '%s' \"$0\"");
        var adapter = new CliModelAdapter("shell", configuration, new ProcessRunner());
        var request = new ModelRequest("r", ConclaveStage.Proposal, "prompt", Environment.CurrentDirectory, "schema", new("shell", "stage-model"));
        var result = await adapter.ExecuteAsync(request, CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal("stage-model", result.Content);
    }

    [Fact]
    public async Task Authentication_failure_is_classified()
    {
        var configuration = Config("echo authentication required >&2; exit 1");
        var adapter = new CliModelAdapter("shell", configuration, new ProcessRunner());
        var request = new ModelRequest("r", ConclaveStage.Review, "prompt", Environment.CurrentDirectory, "schema", new("shell", "m"));
        var result = await adapter.ExecuteAsync(request, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal(ProviderFailureKind.Authentication, result.FailureKind);
    }

    [Fact]
    public async Task Jsonl_output_yields_final_message_and_usage()
    {
        const string script = "printf '%s\\n' '{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"{\\\"summary\\\":\\\"done\\\"}\"}}' '{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":12,\"output_tokens\":3}}'";
        var adapter = new CliModelAdapter("shell", Config(script), new ProcessRunner());
        var activities = new List<ProviderActivity>();
        var request = new ModelRequest("r", ConclaveStage.Synthesis, "prompt", Environment.CurrentDirectory, "schema", new("shell", "m"), Activity: activities.Add);
        var result = await adapter.ExecuteAsync(request, CancellationToken.None);
        Assert.Equal("{\"summary\":\"done\"}", result.Content);
        Assert.Equal(15, result.Usage.KnownTokens);
        Assert.Contains(activities, x => x.Code == "provider_started");
        Assert.Contains(activities, x => x.Code == "response_drafted");
        Assert.Contains(activities, x => x.Code == "response_completed");
        Assert.DoesNotContain(activities, x => x.Message.Contains("done", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Claude_usage_includes_cache_reads_and_total_usd_cost()
    {
        const string script = "printf '%s' '{\"result\":\"{\\\"summary\\\":\\\"done\\\"}\",\"total_cost_usd\":0.12,\"usage\":{\"input_tokens\":10,\"cache_read_input_tokens\":200,\"output_tokens\":5}}'";
        var adapter = new CliModelAdapter("claude", Config(script), new ProcessRunner());
        var result = await adapter.ExecuteAsync(new ModelRequest("r", ConclaveStage.Proposal, "prompt", Environment.CurrentDirectory, "schema", new("claude", "sonnet")), CancellationToken.None);

        Assert.Equal(200, result.Usage.CachedInputTokens);
        Assert.Equal(210, result.Usage.InputTokens);
        Assert.Equal(215, result.Usage.KnownTokens);
        Assert.Equal(0.12m, result.Usage.Cost);
        Assert.Equal("USD", result.Usage.Currency);
    }

    [Fact]
    public async Task Process_timeout_kills_the_process_tree()
    {
        var result = await new ProcessRunner().RunAsync(new ProcessRequest("/bin/sh", ["-c", "sleep 5"], Environment.CurrentDirectory, Timeout: TimeSpan.FromMilliseconds(100)), CancellationToken.None);
        Assert.True(result.TimedOut);
    }

    [Fact]
    public async Task Adapter_passes_only_its_configured_provider_secret()
    {
        var configuration = Config("printf '%s:%s:%s' \"$DEEPSEEK_API_KEY\" \"${ANTHROPIC_API_KEY-unset}\" \"${OPENAI_API_KEY-unset}\"");
        configuration.CredentialEnvironmentVariable = "DEEPSEEK_API_KEY";
        configuration.CredentialValue = "deepseek-test-secret";
        var adapter = new CliModelAdapter("deepseek", configuration, new ProcessRunner());
        var request = new ModelRequest("r", ConclaveStage.Proposal, "prompt", Environment.CurrentDirectory, "schema", new("deepseek", "m"));
        var result = await adapter.ExecuteAsync(request, CancellationToken.None);
        Assert.Equal("[REDACTED]:unset:unset", result.Content);
    }

    [Fact]
    public async Task Provider_probe_does_not_reveal_a_credential_suffix()
    {
        var configuration = Config("true");
        configuration.CredentialEnvironmentVariable = "DEEPSEEK_API_KEY";
        configuration.CredentialValue = "deepseek-test-3456";
        configuration.ProbeArguments = ["-c", "printf 'active source: env (last4: ...3456)'"];
        var adapter = new CliModelAdapter("deepseek", configuration, new ProcessRunner());

        var probe = await adapter.ProbeAsync(CancellationToken.None);

        Assert.True(probe.Available);
        Assert.DoesNotContain("3456", probe.Detail, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", probe.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provider_probe_fails_before_launch_when_a_required_credential_is_missing()
    {
        var configuration = Config("true");
        configuration.CredentialEnvironmentVariable = "DEEPSEEK_API_KEY";
        configuration.CredentialRequired = true;
        var adapter = new CliModelAdapter("deepseek", configuration, new ProcessRunner());

        var probe = await adapter.ProbeAsync(CancellationToken.None);

        Assert.False(probe.Available);
        Assert.Contains("DEEPSEEK_API_KEY", probe.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Adapter_does_not_truncate_large_structured_output_before_parsing()
    {
        var configuration = Config("printf '{\"summary\":\"'; head -c 12000 /dev/zero | tr '\\0' x; printf '\"}'");
        var adapter = new CliModelAdapter("large", configuration, new ProcessRunner());
        var result = await adapter.ExecuteAsync(Request(CreateSchema("""{"type":"object"}""")), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Content!.Length > 12_000);
        Assert.DoesNotContain("[truncated]", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Draft_agnostic_dialect_removes_the_meta_schema_for_claude()
    {
        var configuration = Config("printf '%s' \"$0\"");
        configuration.JsonSchemaDialect = JsonSchemaDialect.DraftAgnostic;
        configuration.Proposal.Arguments = ["-c", "printf '%s' \"$0\"", "{schemaJson}"];
        var adapter = new CliModelAdapter("claude", configuration, new ProcessRunner());
        var result = await adapter.ExecuteAsync(Request(CreateSchema("""{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""")), CancellationToken.None);

        Assert.True(result.Success);
        Assert.DoesNotContain("$schema", result.Content, StringComparison.Ordinal);
        Assert.Contains("\"name\"", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAI_strict_dialect_requires_every_declared_property_recursively()
    {
        var configuration = Config("cat \"$0\"");
        configuration.JsonSchemaDialect = JsonSchemaDialect.OpenAiStrict;
        configuration.Proposal.Arguments = ["-c", "cat \"$0\"", "{schema}"];
        var adapter = new CliModelAdapter("codex", configuration, new ProcessRunner());
        var schema = CreateSchema("""{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","properties":{"optional":{"type":["string","null"]},"nested":{"type":"object","properties":{"value":{"type":"string"}},"required":[]}},"required":[]}""");
        var result = await adapter.ExecuteAsync(Request(schema), CancellationToken.None);

        Assert.True(result.Success);
        using var document = JsonDocument.Parse(result.Content!);
        Assert.False(document.RootElement.TryGetProperty("$schema", out _));
        Assert.Equal(["optional", "nested"], document.RootElement.GetProperty("required").EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.Equal(["value"], document.RootElement.GetProperty("properties").GetProperty("nested").GetProperty("required").EnumerateArray().Select(x => x.GetString()!).ToArray());
    }

    private static ModelRequest Request(string schemaPath) =>
        new("r", ConclaveStage.Proposal, "prompt", Path.GetDirectoryName(schemaPath)!, schemaPath, new("provider", "m"));

    private string CreateSchema(string json)
    {
        Directory.CreateDirectory(Path.Combine(_schemaRoot, ".conclave-input"));
        var path = Path.Combine(_schemaRoot, "schema-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        return path;
    }

    private static ProviderConfiguration Config(string script) => new()
    {
        Command = "/bin/sh",
        PromptTransport = PromptTransport.Stdin,
        Proposal = new() { Model = "m", Arguments = ["-c", script, "{model}"] },
        Review = new() { Model = "m", Arguments = ["-c", script, "{model}"] },
        Synthesis = new() { Model = "m", Arguments = ["-c", script, "{model}"] }
    };
}
