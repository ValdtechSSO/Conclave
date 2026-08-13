using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        var usesSchema = stage.Arguments.Any(x => x.Contains("{schema}", StringComparison.Ordinal) || x.Contains("{schemaJson}", StringComparison.Ordinal));
        var providerSchema = usesSchema ? AdaptSchema(request.OutputSchemaPath, _configuration.JsonSchemaDialect) : "";
        var providerSchemaPath = request.OutputSchemaPath;
        if (usesSchema && _configuration.JsonSchemaDialect != JsonSchemaDialect.Authoritative && stage.Arguments.Any(x => x.Contains("{schema}", StringComparison.Ordinal)))
        {
            providerSchemaPath = Path.Combine(request.WorkingDirectory, ".conclave-input", "provider-output-schema.json");
            await File.WriteAllTextAsync(providerSchemaPath, providerSchema, cancellationToken);
        }
        var arguments = ExpandArguments(stage.Arguments, request, providerSchemaPath, providerSchema);
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
            request.Activity?.Invoke(new("provider_launching", "launching provider with repository search guidance"));
            var activity = new ProviderActivityTranslator(Id, request.Activity);
            var process = await _processRunner.RunAsync(new ProcessRequest(
                _configuration.Command,
                arguments,
                request.WorkingDirectory,
                standardInput,
                Environment: ProviderEnvironment(),
                Timeout: TimeSpan.FromSeconds(_configuration.TimeoutSeconds),
                Activity: activity.Observe), cancellationToken);
            stopwatch.Stop();
            var failure = Classify(process);
            return new ModelExecutionResult(
                request.Participant,
                request.Stage,
                failure == ProviderFailureKind.None,
                failure,
                ExtractContent(Redact(process.StandardOutput, truncate: false)),
                ExtractUsage(process.StandardOutput),
                process.Duration,
                process.ExitCode,
                failure == ProviderFailureKind.None ? null : FailureDetail(process));
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
        if (_configuration.CredentialRequired && string.IsNullOrWhiteSpace(_configuration.CredentialValue))
            return (false, $"Required credential {_configuration.CredentialEnvironmentVariable ?? "is"} not configured.");
        try
        {
            var result = await _processRunner.RunAsync(new ProcessRequest(_configuration.Command, _configuration.ProbeArguments, Environment.CurrentDirectory, Environment: ProviderEnvironment(), Timeout: TimeSpan.FromSeconds(10), MaxOutputCharacters: 20_000), cancellationToken);
            var detail = (result.StandardOutput + " " + result.StandardError).Trim();
            var capabilities = $"schema={_configuration.SupportsSchemaConstrainedOutput.ToString().ToLowerInvariant()}, usage={_configuration.ReportsUsageAfterCall.ToString().ToLowerInvariant()}, realTimeTokenLimit={_configuration.SupportsRealTimeTokenLimit.ToString().ToLowerInvariant()}";
            var failedPattern = _configuration.ProbeFailurePatterns.FirstOrDefault(pattern => detail.Contains(pattern, StringComparison.OrdinalIgnoreCase));
            var safeDetail = Redact(detail.Length == 0 ? $"exit {result.ExitCode}" : detail);
            return (result.ExitCode == 0 && failedPattern is null, safeDetail + $"; {capabilities}");
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
        if (message.Contains("invalid_json_schema", StringComparison.Ordinal) || message.Contains("not a valid json schema", StringComparison.Ordinal)) return ProviderFailureKind.InvalidStructuredOutput;
        if (message.Contains("credit balance", StringComparison.Ordinal) || message.Contains("insufficient credit", StringComparison.Ordinal) || message.Contains("billing", StringComparison.Ordinal)) return ProviderFailureKind.Billing;
        if (message.Contains("rate limit", StringComparison.Ordinal) || message.Contains("too many requests", StringComparison.Ordinal)) return ProviderFailureKind.RateLimit;
        if (message.Contains("unauthorized", StringComparison.Ordinal) || message.Contains("authentication", StringComparison.Ordinal) || message.Contains("login", StringComparison.Ordinal)) return ProviderFailureKind.Authentication;
        if (message.Contains("context length", StringComparison.Ordinal) || message.Contains("context limit", StringComparison.Ordinal)) return ProviderFailureKind.ContextLimit;
        return ProviderFailureKind.ProcessCrash;
    }

    private List<string> ExpandArguments(IReadOnlyList<string> configured, ModelRequest request, string schemaPath, string schemaJson)
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
                .Replace("{schema}", schemaPath, StringComparison.Ordinal)
                .Replace("{maxCostUsd}", _configuration.MaxCostUsd.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
            if (expanded.Contains("{schemaJson}", StringComparison.Ordinal))
                expanded = expanded.Replace("{schemaJson}", schemaJson, StringComparison.Ordinal);
            arguments.Add(expanded);
        }
        return arguments;
    }

    private UsageMetrics ExtractUsage(string output)
    {
        UsageMetrics? last = null;
        foreach (var candidate in JsonCandidates(output))
        {
            try
            {
                using var document = JsonDocument.Parse(candidate);
                if (!FindProperty(document.RootElement, "usage", out var usage)) continue;
                var cost = ReadDecimal(usage, "cost");
                if (cost is null && FindProperty(document.RootElement, "total_cost_usd", out var totalCost) && totalCost.TryGetDecimal(out var totalCostValue)) cost = totalCostValue;
                var input = ReadLong(usage, "inputTokens", "input_tokens");
                var genericCached = ReadLong(usage, "cachedInputTokens", "cached_input_tokens");
                var cacheRead = ReadLong(usage, "cache_read_input_tokens");
                var cacheCreation = ReadLong(usage, "cache_creation_input_tokens");
                var anthropicCached = (cacheRead ?? 0) + (cacheCreation ?? 0);
                var cached = genericCached ?? (cacheRead is null && cacheCreation is null ? null : anthropicCached);
                // Codex reports cached input as a subset of input_tokens. Claude reports
                // cache read/creation separately, so normalize InputTokens to total input.
                if (string.Equals(Id, "claude", StringComparison.OrdinalIgnoreCase) || cacheRead is not null || cacheCreation is not null)
                    input = (input ?? 0) + anthropicCached;
                last = new UsageMetrics(
                    input,
                    cached,
                    ReadLong(usage, "outputTokens", "output_tokens"),
                    cost,
                    cost is null ? ReadString(usage, "currency") : "USD");
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
                else if (root.TryGetProperty("structured_output", out var structured) && structured.ValueKind == JsonValueKind.Object)
                    content = structured.GetRawText();
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

    private string FailureDetail(ProcessResult process)
    {
        var detail = string.Join('\n', new[] { process.StandardError, process.StandardOutput }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return Redact(detail);
    }

    private static string AdaptSchema(string path, JsonSchemaDialect dialect)
    {
        var authoritative = File.ReadAllText(path);
        if (dialect == JsonSchemaDialect.Authoritative) return authoritative;
        var root = JsonNode.Parse(authoritative) ?? throw new InvalidDataException($"Output schema is empty: {path}");
        RemoveDialectDeclarations(root);
        if (dialect == JsonSchemaDialect.OpenAiStrict) MakeObjectsStrict(root);
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static void RemoveDialectDeclarations(JsonNode node)
    {
        if (node is JsonObject value)
        {
            value.Remove("$schema");
            foreach (var child in value.Select(x => x.Value).Where(x => x is not null).ToArray()) RemoveDialectDeclarations(child!);
        }
        else if (node is JsonArray array)
            foreach (var child in array.Where(x => x is not null).ToArray()) RemoveDialectDeclarations(child!);
    }

    private static void MakeObjectsStrict(JsonNode node)
    {
        if (node is JsonObject value)
        {
            if (value["properties"] is JsonObject properties)
            {
                value["additionalProperties"] = false;
                value["required"] = new JsonArray(properties.Select(x => JsonValue.Create(x.Key)).ToArray<JsonNode?>());
            }
            foreach (var child in value.Select(x => x.Value).Where(x => x is not null).ToArray()) MakeObjectsStrict(child!);
        }
        else if (node is JsonArray array)
            foreach (var child in array.Where(x => x is not null).ToArray()) MakeObjectsStrict(child!);
    }

    private string Redact(string value, bool truncate = true)
    {
        var result = value;
        if (!string.IsNullOrEmpty(_configuration.CredentialValue))
        {
            result = result.Replace(_configuration.CredentialValue, "[REDACTED]", StringComparison.Ordinal);
            if (_configuration.CredentialValue.Length >= 4)
            {
                var suffix = _configuration.CredentialValue[^4..];
                result = result.Replace("..." + suffix, "...[REDACTED]", StringComparison.OrdinalIgnoreCase)
                    .Replace("…" + suffix, "…[REDACTED]", StringComparison.OrdinalIgnoreCase);
            }
        }
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = entry.Key?.ToString() ?? "";
            var secret = entry.Value?.ToString();
            if (string.IsNullOrEmpty(secret) || !new[] { "TOKEN", "SECRET", "PASSWORD", "API_KEY", "APIKEY", "CREDENTIAL" }.Any(x => key.Contains(x, StringComparison.OrdinalIgnoreCase))) continue;
            result = result.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
        }
        return !truncate || result.Length <= 8_000 ? result : result[..8_000] + " [truncated]";
    }

    private sealed class ProviderActivityTranslator(string providerId, Action<ProviderActivity>? report)
    {
        private readonly object _gate = new();
        private readonly System.Text.StringBuilder _stdout = new();
        private readonly System.Text.StringBuilder _stderr = new();
        private string? _lastCode;
        private bool _sawOutput;

        public void Observe(ProcessActivity activity)
        {
            lock (_gate)
            {
                switch (activity.Kind)
                {
                    case ProcessActivityKind.Started:
                        Emit("provider_started", "provider process started");
                        break;
                    case ProcessActivityKind.InputDelivered:
                        Emit("prompt_delivered", "task and suggested paths delivered to provider");
                        break;
                    case ProcessActivityKind.StandardOutput:
                        ObserveOutput(activity.Data ?? "", _stdout, standardError: false);
                        break;
                    case ProcessActivityKind.StandardError:
                        ObserveOutput(activity.Data ?? "", _stderr, standardError: true);
                        break;
                    case ProcessActivityKind.Exited:
                        Flush(_stdout, standardError: false);
                        Flush(_stderr, standardError: true);
                        Emit("provider_exited", $"provider process exited with code {activity.Data ?? "unknown"}");
                        break;
                }
            }
        }

        private void ObserveOutput(string chunk, System.Text.StringBuilder buffer, bool standardError)
        {
            if (!_sawOutput && !standardError)
            {
                _sawOutput = true;
                Emit("response_started", "provider started returning a response");
            }
            buffer.Append(chunk);
            while (true)
            {
                var value = buffer.ToString();
                var newline = value.IndexOf('\n');
                if (newline < 0) break;
                ObserveLine(value[..newline].Trim(), standardError);
                buffer.Clear().Append(value[(newline + 1)..]);
            }
        }

        private void Flush(System.Text.StringBuilder buffer, bool standardError)
        {
            if (buffer.Length == 0) return;
            ObserveLine(buffer.ToString().Trim(), standardError);
            buffer.Clear();
        }

        private void ObserveLine(string line, bool standardError)
        {
            if (line.Length == 0) return;
            if (standardError)
            {
                if (line.Contains("error", StringComparison.OrdinalIgnoreCase)) Emit("provider_diagnostic", "provider emitted a diagnostic message");
                return;
            }
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var type = ReadEventType(root);
                var itemType = root.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object && item.TryGetProperty("type", out var nestedType)
                    ? nestedType.GetString()
                    : null;
                Translate(type, itemType);
            }
            catch (JsonException)
            {
                if (string.Equals(providerId, "deepseek", StringComparison.OrdinalIgnoreCase))
                    Emit("response_streaming", "provider is generating the structured response");
            }
        }

        private static string? ReadEventType(JsonElement root)
        {
            if (root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String) return type.GetString();
            return null;
        }

        private void Translate(string? type, string? itemType)
        {
            switch (type)
            {
                case "thread.started":
                case "system":
                    Emit("provider_initialized", "provider initialized the request");
                    break;
                case "turn.started":
                    Emit("scoped_analysis_started", "provider is analyzing the scoped task");
                    break;
                case "assistant":
                case "content_block_start":
                case "content_block_delta":
                case "message_delta":
                    Emit("response_streaming", "provider is generating the structured response");
                    break;
                case "item.started":
                    TranslateItem(itemType, completed: false);
                    break;
                case "item.completed":
                    TranslateItem(itemType, completed: true);
                    break;
                case "turn.completed":
                case "result":
                    Emit("response_completed", "provider completed the structured response");
                    break;
                case "turn.failed":
                case "error":
                    Emit("provider_error", "provider reported an execution error");
                    break;
            }
        }

        private void TranslateItem(string? itemType, bool completed)
        {
            switch (itemType)
            {
                case "reasoning":
                    Emit(completed ? "scoped_analysis_completed" : "scoped_analysis_started", completed ? "provider completed its scoped analysis" : "provider is analyzing the scoped task");
                    break;
                case "agent_message":
                    Emit(completed ? "response_drafted" : "response_streaming", completed ? "provider drafted the structured response" : "provider is drafting the structured response");
                    break;
                case "command_execution":
                case "tool_call":
                    Emit(completed ? "reported_tool_activity_completed" : "reported_tool_activity", completed ? "provider reported completed tool activity" : "provider reported tool activity");
                    break;
            }
        }

        private void Emit(string code, string message)
        {
            if (report is null || string.Equals(_lastCode, code, StringComparison.Ordinal)) return;
            _lastCode = code;
            try { report(new(code, message)); }
            catch { }
        }
    }
}

public sealed class ScriptedModelAdapter(string id, Func<ModelRequest, CancellationToken, Task<ModelExecutionResult>> execute) : IModelAdapter
{
    public string Id { get; } = id;
    public Task<ModelExecutionResult> ExecuteAsync(ModelRequest request, CancellationToken cancellationToken) => execute(request, cancellationToken);
    public Task<(bool Available, string Detail)> ProbeAsync(CancellationToken cancellationToken) => Task.FromResult((true, "scripted test provider"));
}
