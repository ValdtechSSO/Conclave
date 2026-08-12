using Conclave.Core;
using Conclave.Infrastructure;
using Conclave.Orchestration.Features.Operations;
using Conclave.Repository;

namespace Conclave.Orchestration.Tests;

public sealed class OperationalTests
{
    [Fact]
    public async Task Show_reads_run_and_prune_dry_run_is_non_destructive()
    {
        var home = Path.Combine(Path.GetTempPath(), "conclave-operations-" + Guid.NewGuid().ToString("N"));
        try
        {
            var configuration = ConfigurationLoader.Defaults();
            configuration.HomePath = home;
            configuration.Retention.KeepRuns = 1;
            configuration.Retention.MaxAgeDays = 30;
            var store = new FileRunStore(home);
            await WriteRun(store, "recent", DateTimeOffset.UtcNow);
            await WriteRun(store, "old", DateTimeOffset.UtcNow.AddDays(-40));
            var show = await new ShowService(store).GetAsync("old", CancellationToken.None);
            Assert.Equal("old", show!.RunId);

            var process = new ProcessRunner();
            var prune = new PruneService(configuration, store, new GitRepositoryService(process), new GitProviderWorkspaceService(process));
            var dry = await prune.ExecuteAsync(true, CancellationToken.None);
            Assert.Equal(["old"], dry.SelectedRuns);
            Assert.True(Directory.Exists(store.GetRunPath("old")));
            var real = await prune.ExecuteAsync(false, CancellationToken.None);
            Assert.Equal(["old"], real.SelectedRuns);
            Assert.False(Directory.Exists(store.GetRunPath("old")));
            Assert.True(Directory.Exists(store.GetRunPath("recent")));
        }
        finally { if (Directory.Exists(home)) Directory.Delete(home, recursive: true); }
    }

    private static async Task WriteRun(FileRunStore store, string id, DateTimeOffset completed)
    {
        await store.InitializeAsync(id, CancellationToken.None);
        await store.WriteJsonAsync(id, "result.json", new RunResult { RunId = id, RunKey = Guid.NewGuid().ToString("N"), RunPath = store.GetRunPath(id), Status = "completed", StartedAt = completed.AddMinutes(-1), CompletedAt = completed }, CancellationToken.None);
    }
}
