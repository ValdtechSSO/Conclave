using System.Text.Json;
using Conclave.Planning;
using Conclave.Planning.Features.Environment;
using Conclave.Planning.Features.Plan;
using Conclave.Planning.Features.Run;
using Conclave.Planning.Infrastructure;

namespace Conclave.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h" or "help") { Help(); return 0; }
            return args[0] switch
            {
                "plan" => await PlanAsync(args[1..], cancellation.Token),
                "show" => await ShowAsync(args[1..], cancellation.Token),
                "doctor" => await DoctorAsync(args[1..], cancellation.Token),
                "prune" => await PruneAsync(args[1..], cancellation.Token),
                _ => Fail(ConclaveExitCode.InvalidRequest, $"Unknown command '{args[0]}'.")
            };
        }
        catch (OperationCanceledException) { return Fail(ConclaveExitCode.Cancelled, "Cancelled."); }
        catch (ConclaveException exception) { return Fail(exception.ExitCode, exception.Message); }
        catch (ArgumentException exception) { return Fail(ConclaveExitCode.InvalidRequest, exception.Message); }
        catch (InvalidDataException exception) { return Fail(ConclaveExitCode.ConfigurationError, exception.Message); }
        catch (Exception exception) { return Fail(ConclaveExitCode.WorkspaceFailure, exception.Message); }
    }

    private static async Task<int> PlanAsync(string[] args, CancellationToken cancellationToken)
    {
        var options = Arguments.Parse(args, ["json", "keep-workspaces", "development", "no-progress", "whole-repository"]);
        var runId = options.Required("id");
        var directory = Path.GetFullPath(options.Value("directory") ?? ".");
        var prompt = options.Value("prompt");
        var promptFile = options.Value("prompt-file");
        if ((prompt is null) == (promptFile is null)) throw new ArgumentException("Specify exactly one of --prompt or --prompt-file.");
        if (promptFile is not null) prompt = await File.ReadAllTextAsync(Path.GetFullPath(promptFile), cancellationToken);
        var snapshotMode = (options.Value("snapshot") ?? "head").ToLowerInvariant() switch
        {
            "head" => SnapshotMode.Head,
            "working-tree" => SnapshotMode.WorkingTree,
            _ => throw new ArgumentException("--snapshot must be head or working-tree.")
        };
        UnverifiablePolicy? evidencePolicy = (options.Value("evidence-policy") ?? "").ToLowerInvariant() switch
        {
            "" => null,
            "annotate" => UnverifiablePolicy.Annotate,
            "fail" => UnverifiablePolicy.Fail,
            _ => throw new ArgumentException("--evidence-policy must be annotate or fail.")
        };
        var providers = options.Value("providers")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var scope = options.Value("scope")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var configuration = new ConfigurationLoader().Load(directory);
        ApplyModelOverrides(configuration, options.Value("models"));
        if (options.Value("max-cost-usd") is { } maxCostText)
        {
            var maxCost = decimal.Parse(maxCostText, System.Globalization.CultureInfo.InvariantCulture);
            if (maxCost <= 0) throw new ArgumentException("--max-cost-usd must be positive.");
            configuration.RunBudget.MaxCostUsd = maxCost;
            foreach (var provider in configuration.Providers.Values) provider.MaxCostUsd = Math.Min(provider.MaxCostUsd, maxCost);
        }
        var composition = Compose(configuration);
        var request = new ConclaveRequest(runId, directory, prompt!, snapshotMode, options.Value("output"), providers, options.Flag("keep-workspaces"), evidencePolicy, options.Flag("development"), scope, options.Flag("whole-repository"));
        var progressFormat = (options.Value("progress-format") ?? "text").ToLowerInvariant();
        if (progressFormat is not ("text" or "jsonl")) throw new ArgumentException("--progress-format must be text or jsonl.");
        var progressSinks = new List<IConclaveProgressSink> { new JsonlFileProgressSink(Path.Combine(composition.Store.GetRunPath(runId), "progress.jsonl")) };
        if (!options.Flag("no-progress")) progressSinks.Add(new ConsoleProgressSink(progressFormat == "jsonl"));
        IConclaveProgressSink progress = new CompositeProgressSink([.. progressSinks]);
        var planAssetsPath = Path.Combine(AppContext.BaseDirectory, "Modules", "Planning", "Features", "Plan");
        var orchestrator = new PlanOrchestrator(configuration, composition.Adapters, composition.Snapshots, composition.Workspaces, composition.Store, new ArtifactParser(), new ArtifactValidator(), new EvidenceValidator(composition.Snapshots), new MarkdownPlanRenderer(), new BudgetManager(configuration), new RandomShuffler(), planAssetsPath, progress);
        var result = await orchestrator.ExecuteAsync(request, cancellationToken);
        if (options.Flag("json")) Json(result);
        else
        {
            Console.WriteLine($"Conclave run {result.RunId}: {result.Status}");
            Console.WriteLine($"Snapshot: {result.SnapshotSha}");
            Console.WriteLine($"Proposals/reviews: {result.ProposalCount}/{result.ReviewCount}");
            if (result.PlanPath is not null) Console.WriteLine($"Plan: {result.PlanPath}");
            foreach (var warning in result.Warnings) Console.Error.WriteLine($"warning: {warning}");
        }
        return (int)result.ExitCode;
    }

    private static async Task<int> ShowAsync(string[] args, CancellationToken cancellationToken)
    {
        var options = Arguments.Parse(args, ["json", "plan", "progress"], allowPositionals: true);
        var runId = options.Positionals.FirstOrDefault() ?? throw new ArgumentException("show requires a run ID.");
        var configuration = new ConfigurationLoader().Load(Environment.CurrentDirectory);
        var service = new ShowService(new FileRunStore(configuration.HomePath));
        if (options.Flag("progress"))
        {
            var progress = await service.GetProgressAsync(runId, cancellationToken);
            if (progress is null) return Fail(ConclaveExitCode.InvalidRequest, $"Run '{runId}' has no progress events.");
            Console.Write(progress);
            return 0;
        }
        var result = await service.GetAsync(runId, cancellationToken);
        if (result is null) return Fail(ConclaveExitCode.InvalidRequest, $"Run '{runId}' was not found.");
        if (options.Flag("plan"))
        {
            var plan = await service.GetPlanAsync(runId, cancellationToken);
            if (plan is null) return Fail(ConclaveExitCode.InvalidRequest, $"Run '{runId}' has no published plan.");
            if (options.Flag("json")) Json(new { result, plan }); else Console.Write(plan);
        }
        else if (options.Flag("json")) Json(result);
        else
        {
            Console.WriteLine($"Run: {result.RunId}");
            Console.WriteLine($"Status: {result.Status} (exit {(int)result.ExitCode})");
            Console.WriteLine($"Snapshot: {result.SnapshotSha}");
            Console.WriteLine($"Providers: {string.Join(", ", result.Providers)}");
            Console.WriteLine($"Proposals: {result.ProposalCount}; reviews: {result.ReviewCount}");
            Console.WriteLine($"Known tokens: {result.Usage.KnownTokens}");
            Console.WriteLine($"Plan: {result.PlanPath ?? "not available"}");
            foreach (var warning in result.Warnings) Console.WriteLine($"Warning: {warning}");
        }
        return (int)result.ExitCode;
    }

    private static async Task<int> DoctorAsync(string[] args, CancellationToken cancellationToken)
    {
        var options = Arguments.Parse(args, ["json"]);
        var configuration = new ConfigurationLoader().Load(Environment.CurrentDirectory);
        var composition = Compose(configuration);
        var report = await new DoctorService(configuration, composition.Adapters, composition.Processes, composition.Snapshots, composition.Workspaces).ExecuteAsync(cancellationToken);
        if (options.Flag("json")) Json(report);
        else
        {
            Console.WriteLine("Conclave installation\n");
            foreach (var check in report.Checks) Console.WriteLine($"{Mark(check.Success)} {check.Name}: {check.Detail}");
            Console.WriteLine("\nProviders\n");
            foreach (var provider in report.Providers) Console.WriteLine($"{Mark(provider.Success)} {provider.Name}: {provider.Detail}");
            Console.WriteLine($"\nConfiguration\nproposal quorum: {report.MinimumProposalQuorum}\nreview quorum: {report.MinimumReviewQuorum}\nevidence policy: {report.EvidencePolicy}\nrun cost cap: ${configuration.RunBudget.MaxCostUsd:F2}");
            foreach (var provider in configuration.Providers.Where(x => x.Value.Enabled))
                Console.WriteLine($"{provider.Key} models: proposal={provider.Value.Proposal.Model}, review={provider.Value.Review.Model}, synthesis={provider.Value.Synthesis.Model}; call cap=${provider.Value.MaxCostUsd:F2}; timeout={provider.Value.TimeoutSeconds}s");
            Console.WriteLine(report.Ready ? "\nConclave ready." : "\nConclave is not ready.");
        }
        return report.Ready ? 0 : (int)ConclaveExitCode.ConfigurationError;
    }

    private static async Task<int> PruneAsync(string[] args, CancellationToken cancellationToken)
    {
        var options = Arguments.Parse(args, ["json", "dry-run"]);
        var configuration = new ConfigurationLoader().Load();
        var composition = Compose(configuration);
        var report = await new PruneService(configuration, composition.Store, composition.Snapshots, composition.Workspaces).ExecuteAsync(options.Flag("dry-run"), cancellationToken);
        if (options.Flag("json")) Json(report);
        else
        {
            Console.WriteLine($"Runs selected: {report.SelectedRuns.Count}");
            foreach (var run in report.SelectedRuns) Console.WriteLine($"- {run}");
            Console.WriteLine($"Snapshot refs removed: {report.RemovedSnapshotRefs.Count}");
            foreach (var warning in report.Warnings) Console.Error.WriteLine($"warning: {warning}");
        }
        return report.Warnings.Count == 0 ? 0 : (int)ConclaveExitCode.WorkspaceFailure;
    }

    private static Composition Compose(ConclaveConfiguration configuration)
    {
        var processes = new ProcessRunner();
        var snapshots = new GitRepositoryService(processes);
        var workspaces = new GitProviderWorkspaceService(processes);
        var adapters = configuration.Providers.Where(x => x.Value.Enabled).ToDictionary(x => x.Key, x => (IModelAdapter)new CliModelAdapter(x.Key, x.Value, processes), StringComparer.OrdinalIgnoreCase);
        return new(processes, snapshots, workspaces, new FileRunStore(configuration.HomePath), adapters);
    }

    private static void ApplyModelOverrides(ConclaveConfiguration configuration, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        foreach (var assignment in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = assignment.IndexOf('=');
            if (separator <= 0 || separator == assignment.Length - 1) throw new ArgumentException("--models must use provider=model assignments separated by commas.");
            var providerId = assignment[..separator].Trim();
            var model = assignment[(separator + 1)..].Trim();
            if (!configuration.Providers.TryGetValue(providerId, out var provider)) throw new ArgumentException($"Unknown provider in --models: {providerId}.");
            provider.Proposal.Model = model;
            provider.Review.Model = model;
            provider.Synthesis.Model = model;
        }
        foreach (var fallback in configuration.SynthesisFallback)
            if (configuration.Providers.TryGetValue(fallback.Provider, out var provider)) fallback.Model = provider.Synthesis.Model;
    }

    private static void Json<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, ConclaveJson.Options));
    private static string Mark(bool success) => success ? "✓" : "✗";
    private static int Fail(ConclaveExitCode code, string message) { Console.Error.WriteLine($"conclave: {message}"); return (int)code; }

    private static void Help() => Console.WriteLine("""
Conclave — evidence-backed multi-model implementation planning

Usage:
  conclave plan --id <id> --directory <repo> (--prompt <text> | --prompt-file <file>) [options]
  conclave show <id> [--plan | --progress] [--json]
  conclave doctor [--json]
  conclave prune [--dry-run] [--json]

Plan options:
  --providers claude,codex,deepseek
  --models claude=<model>,codex=<model>,deepseek=<model>
  --scope <path,path,...>  Required recommended starting paths for repository exploration
  --whole-repository      Explicitly start exploration at the repository root
  --snapshot head|working-tree
  --max-cost-usd <amount> Maximum reported USD cost for the run
  --evidence-policy annotate|fail
  --output <path>
  --keep-workspaces
  --development        Allow a single-provider development run
  --no-progress        Disable live progress written to stderr
  --progress-format text|jsonl
  --json               Stable machine-readable stdout
""");

    private sealed record Composition(ProcessRunner Processes, GitRepositoryService Snapshots, GitProviderWorkspaceService Workspaces, FileRunStore Store, IReadOnlyDictionary<string, IModelAdapter> Adapters);

    private sealed class Arguments
    {
        private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);
        public List<string> Positionals { get; } = [];
        public string? Value(string name) => _values.GetValueOrDefault(name);
        public string Required(string name) => Value(name) ?? throw new ArgumentException($"--{name} is required.");
        public bool Flag(string name) => _values.ContainsKey(name);

        public static Arguments Parse(string[] args, IReadOnlyCollection<string> flags, bool allowPositionals = false)
        {
            var result = new Arguments();
            for (var index = 0; index < args.Length; index++)
            {
                var token = args[index];
                if (!token.StartsWith("--", StringComparison.Ordinal))
                {
                    if (!allowPositionals) throw new ArgumentException($"Unexpected argument '{token}'.");
                    result.Positionals.Add(token);
                    continue;
                }
                var name = token[2..];
                if (flags.Contains(name, StringComparer.Ordinal)) { result._values[name] = null; continue; }
                if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Option --{name} requires a value.");
                result._values[name] = args[++index];
            }
            return result;
        }
    }
}
