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
        Validate(configuration);
        return configuration;
    }

    public static ConclaveConfiguration Defaults()
    {
        var result = new ConclaveConfiguration();
        result.Providers["claude"] = Provider("claude", ["--print", "--model", "{model}", "--output-format", "json", "--no-session-persistence", "--permission-mode", "acceptEdits", "--json-schema", "{schemaJson}"]);
        result.Providers["claude"].ProbeArguments = ["auth", "status"];
        result.Providers["claude"].CredentialEnvironmentVariable = "ANTHROPIC_API_KEY";
        result.Providers["claude"].SupportsSchemaConstrainedOutput = true;
        result.Providers["claude"].ReportsUsageAfterCall = true;
        result.Providers["codex"] = Provider("codex", ["exec", "--model", "{model}", "--sandbox", "workspace-write", "--skip-git-repo-check", "--ephemeral", "--color", "never", "--output-schema", "{schema}", "--json", "-"]);
        result.Providers["codex"].ProbeArguments = ["login", "status"];
        result.Providers["codex"].CredentialEnvironmentVariable = "OPENAI_API_KEY";
        result.Providers["codex"].SupportsSchemaConstrainedOutput = true;
        result.Providers["codex"].ReportsUsageAfterCall = true;
        result.Providers["deepseek"] = Provider("codewhale", ["--provider", "deepseek", "--model", "{model}", "--telemetry", "false", "exec", "--auto", "--output-format", "text", "{prompt}"]);
        result.Providers["deepseek"].PromptTransport = PromptTransport.Argument;
        result.Providers["deepseek"].ProbeArguments = ["auth", "status", "--provider", "deepseek"];
        result.Providers["deepseek"].ProbeFailurePatterns = ["active source: missing"];
        result.Providers["deepseek"].CredentialEnvironmentVariable = "DEEPSEEK_API_KEY";
        result.SynthesisFallback =
        [
            new() { Provider = "codex", Model = "default" },
            new() { Provider = "claude", Model = "default" },
            new() { Provider = "deepseek", Model = "default" }
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
                    if (!configuration.Providers.ContainsKey(providerId)) configuration.Providers[providerId] = Provider(providerId, []);
                    continue;
                }
                if (providerId is null) continue;
                var provider = configuration.Providers[providerId];
                if (indent == 4 && pair.Value.Length == 0 && Enum.TryParse<ConclaveStage>(pair.Key, true, out var parsedStage))
                {
                    stage = parsedStage;
                    continue;
                }
                if (indent == 4)
                {
                    stage = null;
                    if (pair.Key == "enabled") provider.Enabled = Bool(pair.Value);
                    else if (pair.Key == "command") provider.Command = Text(pair.Value);
                    else if (pair.Key == "timeoutSeconds") provider.TimeoutSeconds = Int(pair.Value);
                    else if (pair.Key == "promptTransport" && Enum.TryParse<PromptTransport>(pair.Value, true, out var transport)) provider.PromptTransport = transport;
                }
                else if (indent >= 6 && stage is not null && pair.Key == "model") provider.For(stage.Value).Model = Text(pair.Value);
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
                case "budget" when subsection == "run": ApplyBudget(configuration.RunBudget, pair); break;
                case "budget" when subsection == "provider": ApplyBudget(configuration.ProviderBudget, pair); break;
                case "budget" when pair.Key == "abortOnExceeded": configuration.AbortOnBudgetExceeded = Bool(pair.Value); break;
                case "synthesis" when subsection == "fallback" && pair.Key == "model" && configuration.SynthesisFallback.Count > 0: configuration.SynthesisFallback[^1].Model = Text(pair.Value); break;
            }
        }
    }

    private static void ApplyBudget(BudgetLimit limit, KeyValuePair<string, string> pair)
    {
        if (pair.Key == "maxTokens") limit.MaxTokens = Long(pair.Value);
        else if (pair.Key == "maxDurationMinutes") limit.MaxDurationMinutes = Int(pair.Value);
        else if (pair.Key == "maxCalls") limit.MaxCalls = Int(pair.Value);
    }

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

    private static void Validate(ConclaveConfiguration configuration)
    {
        if (configuration.MinimumProposalQuorum < 1 || configuration.MinimumReviewQuorum < 1)
            throw new InvalidDataException("Quorum values must be positive.");
        if (configuration.Providers.Values.Any(x => x.Enabled && string.IsNullOrWhiteSpace(x.Command)))
            throw new InvalidDataException("Every enabled provider requires a command.");
        if (configuration.SynthesisFallback.Count == 0)
            throw new InvalidDataException("At least one synthesis fallback participant is required.");
    }
}
