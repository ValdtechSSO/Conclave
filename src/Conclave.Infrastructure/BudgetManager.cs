using System.Diagnostics;
using Conclave.Core;

namespace Conclave.Infrastructure;

public sealed class BudgetManager(ConclaveConfiguration configuration) : IBudgetManager
{
    private sealed class Counter
    {
        public long Tokens;
        public int Calls;
        public TimeSpan Duration;
    }

    private readonly object _gate = new();
    private readonly Stopwatch _run = Stopwatch.StartNew();
    private readonly Dictionary<string, Counter> _providers = new(StringComparer.OrdinalIgnoreCase);
    private long _runTokens;
    private int _runCalls;

    public BudgetDecision CanStart(ModelRequest request)
    {
        lock (_gate)
        {
            if ((_run.Elapsed > TimeSpan.FromMinutes(configuration.RunBudget.MaxDurationMinutes) ||
                _runTokens >= configuration.RunBudget.MaxTokens ||
                _runCalls >= configuration.RunBudget.MaxCalls) && configuration.AbortOnBudgetExceeded)
                return BudgetDecision.Deny(ConclaveExitCode.RunBudgetExceeded, "Run resource budget prevents another provider call.");

            var counter = Get(request.Participant.ProviderId);
            var limit = configuration.Providers.TryGetValue(request.Participant.ProviderId, out var provider) && provider.Budget is not null
                ? provider.Budget
                : configuration.ProviderBudget;
            if ((counter.Duration > TimeSpan.FromMinutes(limit.MaxDurationMinutes) || counter.Tokens >= limit.MaxTokens || counter.Calls >= limit.MaxCalls) && configuration.AbortOnBudgetExceeded)
                return BudgetDecision.Deny(ConclaveExitCode.ProviderBudgetExceeded, $"Provider '{request.Participant.ProviderId}' resource budget prevents another call.");
            return BudgetDecision.Allow();
        }
    }

    public void Record(ModelExecutionResult result)
    {
        lock (_gate)
        {
            var counter = Get(result.Participant.ProviderId);
            var tokens = result.Usage.KnownTokens;
            counter.Tokens += tokens;
            counter.Calls++;
            counter.Duration += result.Duration;
            _runTokens += tokens;
            _runCalls++;
        }
    }

    private Counter Get(string providerId)
    {
        if (!_providers.TryGetValue(providerId, out var counter))
            _providers[providerId] = counter = new Counter();
        return counter;
    }
}
