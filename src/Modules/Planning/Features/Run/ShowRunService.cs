using Conclave.Planning;

namespace Conclave.Planning.Features.Run;

public sealed class ShowService(IRunStore store)
{
    public Task<RunResult?> GetAsync(string runId, CancellationToken cancellationToken) => store.ReadJsonAsync<RunResult>(runId, "result.json", cancellationToken);

    public async Task<string?> GetPlanAsync(string runId, CancellationToken cancellationToken)
    {
        var result = await GetAsync(runId, cancellationToken);
        if (result?.PlanPath is null || !File.Exists(result.PlanPath)) return null;
        return await File.ReadAllTextAsync(result.PlanPath, cancellationToken);
    }

    public async Task<string?> GetProgressAsync(string runId, CancellationToken cancellationToken)
    {
        var path = Path.Combine(store.GetRunPath(runId), "progress.jsonl");
        return File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
    }
}
