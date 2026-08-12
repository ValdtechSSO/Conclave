using System.Diagnostics;
using System.Text.Json;
using Conclave.Core;

namespace Conclave.Providers;

public sealed class CliModelAdapter : IModelAdapter
{
    private readonly ProviderConfiguration _configuration;
    private readonly IProcessRunner _processRunner;

    public CliModelAdapter(string id, ProviderConfiguration configuration, IProcessRunner processRunner)
    {
        Id = id;
        _configuration = configuration;
        _processRunner = processRunner;
    }

    public string Id { get; }

    public async Task<ModelExecutionResult> ExecuteAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        var stage = _configuration.For(request.Stage);
        var arguments = ExpandArguments(stage.Arguments, request);
        string? standardInput = null;
        string? promptFile = null;
        switch (_configuration.PromptTransport)
        {
            case PromptTransport.Stdin:
                standardInput = request.Prompt;
                break;
            case PromptTransport.TemporaryFile:
                var inputDirectory = Path.Combine(request.WorkingDirectory, ".conclave-input");
                Directory.CreateDirectory(inputDirectory);
                promptFile = Path.Combine(inputDirectory, "provider-prompt.md");
                await File.WriteAllTextAsync(promptFile, request.Prompt, cancellationToken);
                if (!arguments.Any(x => x.Contains("{promptFile}", StringComparison.Ordinal))) arguments.Add(promptFile);
                break;
            case PromptTransport.Argument:
                if (!arguments.Any(x => x.Contains("{prompt}", StringComparison.Ordinal))) arguments.Add(request.Prompt);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        if (promptFile is not null) arguments = arguments.Select(x => x.Replace("{promptFile}", promptFile, StringComparison.Ordinal)).ToList();
        if (_configuration.PromptTransport == PromptTransport.Argument) arguments = arguments.Select(x => x.Replace("{prompt}", request.Prompt, StringComparison.Ordinal)).ToList();

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var process = await _processRunner.RunAsync(new ProcessRequest(
                _configuration.Command,
                arguments,
                request.WorkingDirectory,
                standardInput,
                Environment: ProviderEnvironment(),
                Timeout: TimeSpan.FromSeconds(_configuration.TimeoutSeconds)), cancellationToken);
            stopwatch.Stop();
            var failure = Classify(process);
            return new ModelExecutionResult(
                request.Participant,
                request.Stage,
                failure == ProviderFailureKind.None,
                failure,
                ExtractContent(Redact(process.StandardOutput)),
                ExtractUsage(process.StandardOutput),
                process.Duration,
                process.ExitCode,
                failure == ProviderFailureKind.None ? null : Redact(process.StandardError));
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return new ModelExecutionResult(request.Participant, request.Stage, false, ProviderFailureKind.Cancelled, null, new(), stopwatch.Elapsed, null, "Provider invocation cancelled.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            stopwatch.Stop();
            return new ModelExecutionResult(request.Participant, request.Stage, false, ProviderFailureKind.ProcessCrash, null, new(), stopwatch.Elapsed, null, Redact(exception.Message));
        }
    }

    public async Task<(bool Available, string Detail)> ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _processRunner.RunAsync(new ProcessRequest(_configuration.Command, _configuration.ProbeArguments, Environment.CurrentDirectory, Environment: ProviderEnvironment(), Timeout: TimeSpan.FromSeconds(10), MaxOutputCharacters: 20_000), cancellationToken);
            var detail = (result.StandardOutput + " " + result.StandardError).Trim();
            var capabilities = $"schema={_configuration.SupportsSchemaConstrainedOutput.ToString().ToLowerInvariant()}, usage={_configuration.ReportsUsageAfterCall.ToString().ToLowerInvariant()}, realTimeTokenLimit={_configuration.SupportsRealTimeTokenLimit.ToString().ToLowerInvariant()}";
            var failedPattern = _configuration.ProbeFailurePatterns.FirstOrDefault(pattern => detail.Contains(pattern, StringComparison.OrdinalIgnoreCase));
            return (result.ExitCode == 0 && failedPattern is null, (detail.Length == 0 ? $"exit {result.ExitCode}" : detail) + $"; {capabilities}");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return (false, Redact(exception.Message));
        }
    }

    private static ProviderFailureKind Classify(ProcessResult process)
    {
        if (process.Cancelled) return ProviderFailureKind.Cancelled;
        if (process.TimedOut) return ProviderFailureKind.Timeout;
        if (process.ExitCode == 0) return ProviderFailureKind.None;
        var message = (process.StandardError + "\n" + process.StandardOutput).ToLowerInvariant();
        if (message.Contains("rate limit", StringComparison.Ordinal) || message.Contains("too many requests", StringComparison.Ordinal)) return ProviderFailureKind.RateLimit;
        if (message.Contains("unauthorized", StringComparison.Ordinal) || message.Contains("authentication", StringComparison.Ordinal) || message.Contains("login", StringComparison.Ordinal)) return ProviderFailureKind.Authentication;
        if (message.Contains("context length", StringComparison.Ordinal) || message.Contains("context limit", StringComparison.Ordinal)) return ProviderFailureKind.ContextLimit;
        return ProviderFailureKind.ProcessCrash;
    }

    private static List<string> ExpandArguments(IReadOnlyList<string> configured, ModelRequest request)
    {
        var arguments = new List<string>();
        var defaultModel = string.IsNullOrWhiteSpace(request.Participant.ModelId) || string.Equals(request.Participant.ModelId, "default", StringComparison.OrdinalIgnoreCase);
        for (var index = 0; index < configured.Count; index++)
        {
            var value = configured[index];
            if (defaultModel && value == "--model" && index + 1 < configured.Count && configured[index + 1].Contains("{model}", StringComparison.Ordinal))
            {
                index++;
                continue;
            }
            var expanded = value
                .Replace("{model}", request.Participant.ModelId, StringComparison.Ordinal)
                .Replace("{schema}", request.OutputSchemaPath, StringComparison.Ordinal);
            if (expanded.Contains("{schemaJson}", StringComparison.Ordinal))
                expanded = expanded.Replace("{schemaJson}", File.ReadAllText(request.OutputSchemaPath), StringComparison.Ordinal);
            arguments.Add(expanded);
        }
        return arguments;
    }

    private static UsageMetrics ExtractUsage(string output)
    {
        UsageMetrics? last = null;
        foreach (var candidate in JsonCandidates(output))
        {
            try
            {
                using var document = JsonDocument.Parse(candidate);
                if (!FindProperty(document.RootElement, "usage", out var usage)) continue;
                last = new UsageMetrics(
                    ReadLong(usage, "inputTokens", "input_tokens"),
                    ReadLong(usage, "cachedInputTokens", "cached_input_tokens"),
                    ReadLong(usage, "outputTokens", "output_tokens"),
                    ReadDecimal(usage, "cost"),
                    ReadString(usage, "currency"));
            }
            catch (JsonException) { }
        }
        return last ?? new();
    }

    private static string ExtractContent(string output)
    {
        try
        {
            using var whole = JsonDocument.Parse(output);
            if (LooksLikeArtifact(whole.RootElement)) return output;
        }
        catch (JsonException) { }
        string? content = null;
        foreach (var candidate in JsonCandidates(output))
        {
            try
            {
                using var document = JsonDocument.Parse(candidate);
                var root = document.RootElement;
                if (root.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object && item.TryGetProperty("text", out var itemText) && itemText.ValueKind == JsonValueKind.String)
                    content = itemText.GetString();
                else if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String)
                    content = result.GetString();
                else if (root.TryGetProperty("content", out var direct) && direct.ValueKind == JsonValueKind.String)
                    content = direct.GetString();
                else if (LooksLikeArtifact(root)) content = candidate;
            }
            catch (JsonException) { }
        }
        return content ?? output;
    }

    private static IEnumerable<string> JsonCandidates(string output)
    {
        yield return output;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries)) yield return line.Trim();
    }

    private static bool LooksLikeArtifact(JsonElement root) => root.ValueKind == JsonValueKind.Object &&
        (root.TryGetProperty("summary", out _) || root.TryGetProperty("goal", out _));

    private static bool FindProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(name, out value)) return true;
            foreach (var property in element.EnumerateObject())
                if (FindProperty(property.Value, name, out value)) return true;
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray())
                if (FindProperty(child, name, out value)) return true;
        value = default;
        return false;
    }

    private static long? ReadLong(JsonElement value, params string[] names)
    {
        foreach (var name in names)
            if (value.TryGetProperty(name, out var property) && property.TryGetInt64(out var result)) return result;
        return null;
    }

    private static decimal? ReadDecimal(JsonElement value, string name) => value.TryGetProperty(name, out var property) && property.TryGetDecimal(out var result) ? result : null;
    private static string? ReadString(JsonElement value, string name) => value.TryGetProperty(name, out var property) ? property.GetString() : null;

    private Dictionary<string, string?> ProviderEnvironment()
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ANTHROPIC_API_KEY"] = null,
            ["DEEPSEEK_API_KEY"] = null,
            ["OPENAI_API_KEY"] = null
        };
        if (_configuration.CredentialEnvironmentVariable is { } name && !string.IsNullOrWhiteSpace(_configuration.CredentialValue))
            environment[name] = _configuration.CredentialValue;
        return environment;
    }

    private string Redact(string value)
    {
        var result = value;
        if (!string.IsNullOrEmpty(_configuration.CredentialValue))
            result = result.Replace(_configuration.CredentialValue, "[REDACTED]", StringComparison.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = entry.Key?.ToString() ?? "";
            var secret = entry.Value?.ToString();
            if (string.IsNullOrEmpty(secret) || !new[] { "TOKEN", "SECRET", "PASSWORD", "API_KEY", "APIKEY", "CREDENTIAL" }.Any(x => key.Contains(x, StringComparison.OrdinalIgnoreCase))) continue;
            result = result.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
        }
        return result.Length <= 8_000 ? result : result[..8_000] + " [truncated]";
    }
}

public sealed class ScriptedModelAdapter(string id, Func<ModelRequest, CancellationToken, Task<ModelExecutionResult>> execute) : IModelAdapter
{
    public string Id { get; } = id;
    public Task<ModelExecutionResult> ExecuteAsync(ModelRequest request, CancellationToken cancellationToken) => execute(request, cancellationToken);
    public Task<(bool Available, string Detail)> ProbeAsync(CancellationToken cancellationToken) => Task.FromResult((true, "scripted test provider"));
}
