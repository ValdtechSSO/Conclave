using Conclave.Core;
using Conclave.Infrastructure;
using Conclave.Providers;

namespace Conclave.Providers.Tests;

public sealed class ProviderTests
{
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
        var request = new ModelRequest("r", ConclaveStage.Synthesis, "prompt", Environment.CurrentDirectory, "schema", new("shell", "m"));
        var result = await adapter.ExecuteAsync(request, CancellationToken.None);
        Assert.Equal("{\"summary\":\"done\"}", result.Content);
        Assert.Equal(15, result.Usage.KnownTokens);
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

    private static ProviderConfiguration Config(string script) => new()
    {
        Command = "/bin/sh",
        PromptTransport = PromptTransport.Stdin,
        Proposal = new() { Model = "m", Arguments = ["-c", script, "{model}"] },
        Review = new() { Model = "m", Arguments = ["-c", script, "{model}"] },
        Synthesis = new() { Model = "m", Arguments = ["-c", script, "{model}"] }
    };
}
