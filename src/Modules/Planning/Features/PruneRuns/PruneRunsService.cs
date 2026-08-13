using Conclave.Planning;

namespace Conclave.Planning.Features.PruneRuns;

public sealed class PruneReport
{
    public bool DryRun { get; set; }
    public List<string> SelectedRuns { get; set; } = [];
    public List<string> RemovedSnapshotRefs { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class PruneService(
    ConclaveConfiguration configuration,
    IRunStore store,
    IRepositorySnapshotService snapshots,
    IProviderWorkspaceService workspaces)
{
    public async Task<PruneReport> ExecuteAsync(bool dryRun, CancellationToken cancellationToken)
    {
        var report = new PruneReport { DryRun = dryRun };
        var runs = new List<RunResult>();
        foreach (var id in await store.ListRunIdsAsync(cancellationToken))
        {
            var run = await store.ReadJsonAsync<RunResult>(id, "result.json", cancellationToken);
            if (run is not null && run.Status is "completed" or "failed") runs.Add(run);
        }
        var ordered = runs.OrderByDescending(x => x.CompletedAt ?? x.StartedAt).ToArray();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-configuration.Retention.MaxAgeDays);
        var selected = ordered.Skip(configuration.Retention.KeepRuns)
            .Concat(ordered.Where(x => (x.CompletedAt ?? x.StartedAt) < cutoff))
            .DistinctBy(x => x.RunId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        report.SelectedRuns.AddRange(selected.Select(x => x.RunId));
        if (dryRun) return report;

        foreach (var run in selected)
        {
            try
            {
                if (run.RepositoryPath is not null && run.SnapshotSha is not null && run.SnapshotRef is not null)
                {
                    var snapshot = new RepositorySnapshot(run.RunKey, run.RepositoryPath, run.SnapshotSha, run.SnapshotSha, run.SnapshotRef, SnapshotMode.Head, false, false);
                    var workspaceRoot = Path.Combine(run.RunPath, "workspaces");
                    if (Directory.Exists(workspaceRoot))
                        foreach (var path in Directory.GetDirectories(workspaceRoot))
                            await workspaces.RemoveAsync(snapshot, new ProviderWorkspace(Path.GetFileName(path), path, run.SnapshotSha), cancellationToken);
                    await workspaces.PruneMetadataAsync(run.RepositoryPath, cancellationToken);
                    await snapshots.DeleteSnapshotRefAsync(run.RepositoryPath, run.SnapshotRef, cancellationToken);
                    report.RemovedSnapshotRefs.Add(run.SnapshotRef);
                }
                var fullRunPath = Path.GetFullPath(run.RunPath);
                var expectedRoot = Path.GetFullPath(Path.Combine(configuration.HomePath, "runs")) + Path.DirectorySeparatorChar;
                if (!fullRunPath.StartsWith(expectedRoot, StringComparison.Ordinal)) throw new InvalidOperationException("Run path is outside Conclave home.");
                if (Directory.Exists(fullRunPath)) Directory.Delete(fullRunPath, recursive: true);
            }
            catch (Exception exception) { report.Warnings.Add($"{run.RunId}: {exception.Message}"); }
        }
        return report;
    }
}
