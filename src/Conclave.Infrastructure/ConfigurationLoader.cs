using Conclave.Core;

namespace Conclave.Infrastructure;

public sealed class ConfigurationLoader
{
    private static readonly HashSet<string> AllowedSecretNames = ["ANTHROPIC_API_KEY", "DEEPSEEK_API_KEY", "OPENAI_API_KEY"];

    public ConclaveConfiguration Load(string? repositoryPath = null)
    {
        var configuration = Defaults();
        var configuredHome = Environment.GetEnvironmentVariable("CONCLAVE_HOME");
        if (!string.IsNullOrWhiteSpace(configuredHome)) configuration.HomePath = Path.GetFullPath(configuredHome);

        var userConfig = Path.Combine(configuration.HomePath, "config.yaml");
        if (File.Exists(userConfig)) Apply(configuration, userConfig);
        if (!string.IsNullOrWhiteSpace(repositoryPath))
        {
            var repositoryConfig = Path.Combine(Path.GetFullPath(repositoryPath), ".conclave.yaml");
            if (File.Exists(repositoryConfig)) Apply(configuration, repositoryConfig);
        }
        LoadSecrets(configuration, repositoryPath);
        foreach (var candidate in configuration.SynthesisFallback)
            if (configuration.Providers.TryGetValue(candidate.Provider, out var provider)) candidate.Model = provider.Synthesis.Model;
        Validate(configuration);
        return configuration;
    }

    public static ConclaveConfiguration Defaults()
    {
        var result = new ConclaveConfiguration();
        result.Providers["claude"] = Provider("claude", ["--print", "--model", "{model}", "--output-format", "stream-json", "--verbose", "--no-session-persistence", "--safe-mode", "--disable-slash-commands", "--strict-mcp-config", "--mcp-config", "{}", "--permission-mode", "dontAsk", "--tools", "Read", "Grep", "Glob", "--max-budget-usd", "{maxCostUsd}", "--json-schema", "{schemaJson}"]);
        SetModels(result.Providers["claude"], "sonnet");
        result.Providers["claude"].ProbeArguments = ["auth", "status"];
        result.Providers["claude"].CredentialEnvironmentVariable = "ANTHROPIC_API_KEY";
        result.Providers["claude"].JsonSchemaDialect = JsonSchemaDialect.DraftAgnostic;
        result.Providers["claude"].SupportsSchemaConstrainedOutput = true;
        result.Providers["claude"].ReportsUsageAfterCall = true;
        result.Providers["claude"].Budget = ProviderBudget(1_000_000, 15);
        result.Providers["codex"] = Provider("codex", CodexArguments());
        SetModels(result.Providers["codex"], "gpt-5.6-sol");
        result.Providers["codex"].ProbeArguments = ["login", "status"];
        result.Providers["codex"].CredentialEnvironmentVariable = "OPENAI_API_KEY";
        result.Providers["codex"].JsonSchemaDialect = JsonSchemaDialect.OpenAiStrict;
        result.Providers["codex"].SupportsSchemaConstrainedOutput = true;
        result.Providers["codex"].ReportsUsageAfterCall = true;
        result.Providers["codex"].Budget = ProviderBudget(1_000_000, 15);
        result.Providers["deepseek"] = Provider("codex", DeepSeekCodexArguments());
        result.Providers["deepseek"].TimeoutSeconds = 600;
        SetModels(result.Providers["deepseek"], "deepseek-v4-flash");
        result.Providers["deepseek"].ProbeArguments = ["--version"];
        result.Providers["deepseek"].CredentialEnvironmentVariable = "DEEPSEEK_API_KEY";
        result.Providers["deepseek"].CredentialRequired = true;
        result.Providers["deepseek"].JsonSchemaDialect = JsonSchemaDialect.OpenAiStrict;
        result.Providers["deepseek"].SupportsSchemaConstrainedOutput = true;
        result.Providers["deepseek"].ReportsUsageAfterCall = true;
        result.Providers["deepseek"].Budget = ProviderBudget(4_000_000, 30);
        result.SynthesisFallback =
        [
            new() { Provider = "codex", Model = "gpt-5.6-sol" },
            new() { Provider = "claude", Model = "sonnet" },
            new() { Provider = "deepseek", Model = "deepseek-v4-flash" }
        ];
        return result;
    }

    private static ProviderConfiguration Provider(string command, List<string> arguments) => new()
    {
        Command = command,
        Proposal = new() { Model = "default", Arguments = [.. arguments] },
        Review = new() { Model = "default", Arguments = [.. arguments] },
        Synthesis = new() { Model = "default", Arguments = [.. arguments] }
    };

    private static BudgetLimit ProviderBudget(long maxTokens, int maxDurationMinutes) => new()
    {
        MaxTokens = maxTokens,
        MaxDurationMinutes = maxDurationMinutes,
        MaxCalls = 3,
        MaxCostUsd = 0.25m
    };

    private static List<string> CodexArguments() =>
    [
        "exec", "--model", "{model}", "--sandbox", "read-only", "--skip-git-repo-check",
        "--ephemeral", "--ignore-user-config", "--ignore-rules",
        "--disable", "apps", "--disable", "plugins", "--disable", "remote_plugin",
        "--disable", "multi_agent", "--disable", "browser_use", "--disable", "in_app_browser",
        "--disable", "computer_use", "--disable", "image_generation",
        "--color", "never", "--output-schema", "{schema}", "--json",
        "-c", "project_doc_max_bytes=0", "-c", "project_doc_fallback_filenames=[]", "-"
    ];

    private static List<string> DeepSeekCodexArguments()
    {
        var catalogPath = Path.Combine(AppContext.BaseDirectory, "config", "deepseek-models.json")
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        var arguments = CodexArguments();
        arguments.InsertRange(arguments.Count - 1,
        [
            "-c", "model_provider=\"deepseek\"",
            "-c", "model_providers.deepseek.name=\"DeepSeek\"",
            "-c", "model_providers.deepseek.base_url=\"https://api.deepseek.com\"",
            "-c", "model_providers.deepseek.env_key=\"DEEPSEEK_API_KEY\"",
            "-c", "model_providers.deepseek.wire_api=\"responses\"",
            "-c", "model_providers.deepseek.requires_openai_auth=false",
            "-c", "model_providers.deepseek.request_max_retries=0",
            "-c", "model_providers.deepseek.stream_max_retries=0",
            "-c", "model_reasoning_effort=\"high\"",
            "-c", "model_verbosity=\"low\"",
            "-c", $"model_catalog_json=\"{catalogPath}\""
        ]);
        return arguments;
    }

    private static void SetModels(ProviderConfiguration provider, string model)
    {
        provider.Proposal.Model = model;
        provider.Review.Model = model;
        provider.Synthesis.Model = model;
    }

    private static void Apply(ConclaveConfiguration configuration, string path)
    {
        var section = "";
        string? providerId = null;
        ConclaveStage? stage = null;
        string? subsection = null;
        foreach (var raw in File.ReadLines(path))
        {
            var withoutComment = raw.Split('#', 2)[0];
            if (string.IsNullOrWhiteSpace(withoutComment)) continue;
            var indent = withoutComment.TakeWhile(char.IsWhiteSpace).Count();
            var line = withoutComment.Trim();
            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                if (section == "synthesis" && subsection == "fallback")
                {
                    var value = ParsePair(line[2..]);
                    if (value.Key == "provider") configuration.SynthesisFallback.Add(new SynthesisParticipant { Provider = value.Value });
                }
                continue;
            }

            var pair = ParsePair(line);
            if (indent == 0)
            {
                section = pair.Key;
                providerId = null;
                stage = null;
                subsection = null;
                if (section == "synthesis" && pair.Value.Length == 0) configuration.SynthesisFallback.Clear();
                continue;
            }

            if (section == "providers")
            {
                if (indent == 2)
                {
                    providerId = pair.Key;
                    stage = null;
                    subsection = null;
                    if (!configuration.Providers.ContainsKey(providerId)) configuration.Providers[providerId] = Provider(providerId, []);
                    continue;
                }
                if (providerId is null) continue;
                var provider = configuration.Providers[providerId];
                if (indent == 4 && pair.Value.Length == 0 && Enum.TryParse<ConclaveStage>(pair.Key, true, out var parsedStage))
                {
                    stage = parsedStage;
                    subsection = null;
                    continue;
                }
                if (indent == 4 && pair.Value.Length == 0 && pair.Key == "budget")
                {
                    stage = null;
                    subsection = "provider-budget";
                    provider.Budget ??= CopyBudget(configuration.ProviderBudget);
                    continue;
                }
                if (indent == 4)
                {
                    stage = null;
                    subsection = null;
                    if (pair.Key == "enabled") provider.Enabled = Bool(pair.Value);
                    else if (pair.Key == "command") provider.Command = Text(pair.Value);
                    else if (pair.Key == "timeoutSeconds") provider.TimeoutSeconds = Int(pair.Value);
                    else if (pair.Key == "maxCostUsd") provider.MaxCostUsd = Decimal(pair.Value);
                    else if (pair.Key == "promptTransport" && Enum.TryParse<PromptTransport>(pair.Value, true, out var transport)) provider.PromptTransport = transport;
                    else if (pair.Key == "jsonSchemaDialect" && Enum.TryParse<JsonSchemaDialect>(pair.Value, true, out var dialect)) provider.JsonSchemaDialect = dialect;
                }
                else if (indent >= 6 && stage is not null && pair.Key == "model") provider.For(stage.Value).Model = Text(pair.Value);
                else if (indent >= 6 && subsection == "provider-budget") ApplyBudget(provider.Budget!, pair);
                continue;
            }

            if (indent == 2 && pair.Value.Length == 0) subsection = pair.Key;
            switch (section)
            {
                case "conclave" when pair.Key == "minimumProposalQuorum": configuration.MinimumProposalQuorum = Int(pair.Value); break;
                case "conclave" when pair.Key == "minimumReviewQuorum": configuration.MinimumReviewQuorum = Int(pair.Value); break;
                case "evidence" when pair.Key == "unverifiablePolicy" && Enum.TryParse<UnverifiablePolicy>(pair.Value, true, out var policy): configuration.EvidencePolicy = policy; break;
                case "retention" when pair.Key == "keepRuns": configuration.Retention.KeepRuns = Int(pair.Value); break;
                case "retention" when pair.Key == "maxAgeDays": configuration.Retention.MaxAgeDays = Int(pair.Value); break;
                case "retention" when pair.Key == "keepWorkspaces": configuration.Retention.KeepWorkspaces = Bool(pair.Value); break;
                case "retry" when pair.Key == "rateLimitAttempts": configuration.Retry.RateLimitAttempts = Int(pair.Value); break;
                case "retry" when pair.Key == "timeoutAttempts": configuration.Retry.TimeoutAttempts = Int(pair.Value); break;
                case "retry" when pair.Key == "invalidStructuredOutputAttempts": configuration.Retry.InvalidStructuredOutputAttempts = Int(pair.Value); break;
                case "retry" when pair.Key == "processCrashAttempts": configuration.Retry.ProcessCrashAttempts = Int(pair.Value); break;
                case "budget" when subsection == "run": ApplyRunBudget(configuration.RunBudget, pair); break;
                case "budget" when subsection == "provider": ApplyBudget(configuration.ProviderBudget, pair); break;
                case "budget" when pair.Key == "abortOnExceeded": configuration.AbortOnBudgetExceeded = Bool(pair.Value); break;
                case "search" when pair.Key == "maxSuggestedRoots": configuration.Search.MaxSuggestedRoots = Int(pair.Value); break;
                case "synthesis" when subsection == "fallback" && pair.Key == "model" && configuration.SynthesisFallback.Count > 0: configuration.SynthesisFallback[^1].Model = Text(pair.Value); break;
            }
        }
    }

    private static void ApplyBudget(BudgetLimit limit, KeyValuePair<string, string> pair)
    {
        if (pair.Key == "maxTokens") limit.MaxTokens = Long(pair.Value);
        else if (pair.Key == "maxDurationMinutes") limit.MaxDurationMinutes = Int(pair.Value);
        else if (pair.Key == "maxCalls") limit.MaxCalls = Int(pair.Value);
        else if (pair.Key == "maxCostUsd") limit.MaxCostUsd = Decimal(pair.Value);
    }

    private static void ApplyRunBudget(RunBudgetLimit limit, KeyValuePair<string, string> pair)
    {
        if (pair.Key == "maxDurationMinutes") limit.MaxDurationMinutes = Int(pair.Value);
        else if (pair.Key == "maxCalls") limit.MaxCalls = Int(pair.Value);
        else if (pair.Key == "maxCostUsd") limit.MaxCostUsd = Decimal(pair.Value);
        else if (pair.Key == "maxTokens")
            throw new InvalidDataException("budget.run.maxTokens is no longer supported; configure providers.<id>.budget.maxTokens instead.");
    }

    private static BudgetLimit CopyBudget(BudgetLimit source) => new()
    {
        MaxTokens = source.MaxTokens,
        MaxDurationMinutes = source.MaxDurationMinutes,
        MaxCalls = source.MaxCalls,
        MaxCostUsd = source.MaxCostUsd
    };

    private static void LoadSecrets(ConclaveConfiguration configuration, string? repositoryPath)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        ApplySecrets(values, Path.Combine(configuration.HomePath, "secrets.env"));
        if (!string.IsNullOrWhiteSpace(repositoryPath))
            ApplySecrets(values, Path.Combine(Path.GetFullPath(repositoryPath), ".conclave.secrets.env"));

        foreach (var provider in configuration.Providers.Values)
        {
            var variable = provider.CredentialEnvironmentVariable;
            if (variable is null) continue;
            var environmentValue = Environment.GetEnvironmentVariable(variable);
            provider.CredentialValue = !string.IsNullOrWhiteSpace(environmentValue)
                ? environmentValue
                : values.GetValueOrDefault(variable);
        }
    }

    private static void ApplySecrets(Dictionary<string, string> values, string path)
    {
        if (!File.Exists(path)) return;
        foreach (var (raw, index) in File.ReadLines(path).Select((line, index) => (line, index + 1)))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..].TrimStart();
            var separator = line.IndexOf('=');
            if (separator <= 0) throw new InvalidDataException($"Invalid secret assignment at {path}:{index}.");
            var name = line[..separator].Trim();
            if (!AllowedSecretNames.Contains(name)) throw new InvalidDataException($"Secret '{name}' is not allowed at {path}:{index}.");
            var value = Unquote(line[(separator + 1)..].Trim());
            if (value.Length > 0) values[name] = value;
        }
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            return value[1..^1];
        return value;
    }

    private static KeyValuePair<string, string> ParsePair(string line)
    {
        var index = line.IndexOf(':');
        if (index < 0) throw new InvalidDataException($"Invalid configuration line: {line}");
        return new(line[..index].Trim(), line[(index + 1)..].Trim());
    }

    private static string Text(string value) => value.Trim().Trim('"', '\'');
    private static bool Bool(string value) => bool.Parse(Text(value));
    private static int Int(string value) => int.Parse(Text(value), System.Globalization.CultureInfo.InvariantCulture);
    private static long Long(string value) => long.Parse(Text(value), System.Globalization.CultureInfo.InvariantCulture);
    private static decimal Decimal(string value) => decimal.Parse(Text(value), System.Globalization.CultureInfo.InvariantCulture);

    private static void Validate(ConclaveConfiguration configuration)
    {
        if (configuration.MinimumProposalQuorum < 1 || configuration.MinimumReviewQuorum < 1)
            throw new InvalidDataException("Quorum values must be positive.");
        if (configuration.Providers.Values.Any(x => x.Enabled && string.IsNullOrWhiteSpace(x.Command)))
            throw new InvalidDataException("Every enabled provider requires a command.");
        if (configuration.SynthesisFallback.Count == 0)
            throw new InvalidDataException("At least one synthesis fallback participant is required.");
        if (configuration.Search.MaxSuggestedRoots < 1)
            throw new InvalidDataException("The suggested-root limit must be positive.");
        if (configuration.Providers.Values.Any(x => x.Enabled && (x.TimeoutSeconds < 1 || x.MaxCostUsd <= 0)))
            throw new InvalidDataException("Enabled providers require positive timeout and cost limits.");
        if (configuration.Providers.Values.Any(x => x.Enabled && x.Budget is { } budget &&
            (budget.MaxTokens < 1 || budget.MaxDurationMinutes < 1 || budget.MaxCalls < 1 || budget.MaxCostUsd <= 0)))
            throw new InvalidDataException("Configured provider budgets require positive token, duration, call, and cost limits.");
    }
}
