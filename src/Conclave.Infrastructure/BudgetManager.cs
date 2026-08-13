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
        public decimal CostUsd;
    }

    private readonly object _gate = new();
    private readonly Stopwatch _run = Stopwatch.StartNew();
    private readonly Dictionary<string, Counter> _providers = new(StringComparer.OrdinalIgnoreCase);
    private int _runCalls;
    private decimal _runCostUsd;

    public BudgetDecision CanStart(ModelRequest request)
    {
        lock (_gate)
        {
            if ((_run.Elapsed > TimeSpan.FromMinutes(configuration.RunBudget.MaxDurationMinutes) ||
                _runCalls >= configuration.RunBudget.MaxCalls ||
                _runCostUsd >= configuration.RunBudget.MaxCostUsd) && configuration.AbortOnBudgetExceeded)
                return BudgetDecision.Deny(ConclaveExitCode.RunBudgetExceeded, "Run resource budget prevents another provider call.");

            var counter = Get(request.Participant.ProviderId);
            var limit = configuration.Providers.TryGetValue(request.Participant.ProviderId, out var provider) && provider.Budget is not null
                ? provider.Budget
                : configuration.ProviderBudget;
            if ((counter.Duration > TimeSpan.FromMinutes(limit.MaxDurationMinutes) || counter.Tokens >= limit.MaxTokens || counter.Calls >= limit.MaxCalls || counter.CostUsd >= limit.MaxCostUsd) && configuration.AbortOnBudgetExceeded)
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
            counter.CostUsd += Usd(result.Usage);
            _runCalls++;
            _runCostUsd += Usd(result.Usage);
        }
    }

    private static decimal Usd(UsageMetrics usage) => string.Equals(usage.Currency, "USD", StringComparison.OrdinalIgnoreCase) ? usage.Cost ?? 0 : 0;

    private Counter Get(string providerId)
    {
        if (!_providers.TryGetValue(providerId, out var counter))
            _providers[providerId] = counter = new Counter();
        return counter;
    }
}
