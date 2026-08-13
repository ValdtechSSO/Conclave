using Conclave.Planning;

namespace Conclave.Planning.Features.Environment;

public sealed class DoctorCheck
{
    public string Name { get; set; } = "";
    public bool Success { get; set; }
    public string Detail { get; set; } = "";
}

public sealed class DoctorReport
{
    public bool Ready => Checks.All(x => x.Success) && Providers.Count(x => x.Success) >= MinimumProposalQuorum;
    public int MinimumProposalQuorum { get; set; }
    public int MinimumReviewQuorum { get; set; }
    public string EvidencePolicy { get; set; } = "";
    public List<DoctorCheck> Checks { get; set; } = [];
    public List<DoctorCheck> Providers { get; set; } = [];
}

public sealed class DoctorService(
    ConclaveConfiguration configuration,
    IReadOnlyDictionary<string, IModelAdapter> adapters,
    IProcessRunner processes,
    IRepositorySnapshotService snapshots,
    IProviderWorkspaceService workspaces)
{
    public async Task<DoctorReport> ExecuteAsync(CancellationToken cancellationToken)
    {
        var report = new DoctorReport
        {
            MinimumProposalQuorum = configuration.MinimumProposalQuorum,
            MinimumReviewQuorum = configuration.MinimumReviewQuorum,
            EvidencePolicy = configuration.EvidencePolicy.ToString().ToLowerInvariant()
        };
        var git = await processes.RunAsync(new ProcessRequest("git", ["--version"], System.Environment.CurrentDirectory, Timeout: TimeSpan.FromSeconds(10)), cancellationToken);
        report.Checks.Add(new() { Name = "Git", Success = git.ExitCode == 0, Detail = (git.StandardOutput + git.StandardError).Trim() });
        report.Checks.Add(CheckHome());
        report.Checks.Add(await CheckSnapshotAndWorktreeAsync(cancellationToken));

        var probes = await Task.WhenAll(adapters.Values.Select(async adapter =>
        {
            var probe = await adapter.ProbeAsync(cancellationToken);
            return new DoctorCheck { Name = adapter.Id, Success = probe.Available, Detail = probe.Detail };
        }));
        report.Providers.AddRange(probes.OrderBy(x => x.Name, StringComparer.Ordinal));
        return report;
    }

    private DoctorCheck CheckHome()
    {
        try
        {
            Directory.CreateDirectory(configuration.HomePath);
            var probe = Path.Combine(configuration.HomePath, ".write-probe-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return new() { Name = "Conclave home", Success = true, Detail = configuration.HomePath };
        }
        catch (Exception exception) { return new() { Name = "Conclave home", Success = false, Detail = exception.Message }; }
    }

    private async Task<DoctorCheck> CheckSnapshotAndWorktreeAsync(CancellationToken cancellationToken)
    {
        var temp = Path.Combine(Path.GetTempPath(), "conclave-doctor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        RepositorySnapshot? snapshot = null;
        ProviderWorkspace? workspace = null;
        try
        {
            Ensure(await GitAsync(temp, ["init"], cancellationToken));
            await File.WriteAllTextAsync(Path.Combine(temp, "README.md"), "doctor", cancellationToken);
            Ensure(await GitAsync(temp, ["add", "README.md"], cancellationToken));
            Ensure(await GitAsync(temp, ["-c", "user.name=Conclave Doctor", "-c", "user.email=doctor@local.invalid", "commit", "-m", "doctor fixture"], cancellationToken));
            snapshot = await snapshots.CreateAsync(temp, Guid.NewGuid().ToString("N"), SnapshotMode.Head, cancellationToken);
            workspace = await workspaces.CreateAsync(snapshot, "doctor", Path.Combine(temp, ".doctor-worktree"), cancellationToken);
            await workspaces.ResetAsync(workspace, cancellationToken);
            await workspaces.RemoveAsync(snapshot, workspace, cancellationToken);
            workspace = null;
            var retained = await snapshots.SnapshotRefMatchesAsync(snapshot, cancellationToken);
            await snapshots.DeleteSnapshotRefAsync(temp, snapshot.SnapshotRef, cancellationToken);
            return new() { Name = "Snapshot and worktree", Success = retained, Detail = retained ? "create/reset/remove/ref lifecycle passed" : "snapshot ref mismatch" };
        }
        catch (Exception exception) { return new() { Name = "Snapshot and worktree", Success = false, Detail = exception.Message }; }
        finally
        {
            if (workspace is not null && snapshot is not null) try { await workspaces.RemoveAsync(snapshot, workspace, CancellationToken.None); } catch { }
            if (snapshot is not null) try { await snapshots.DeleteSnapshotRefAsync(temp, snapshot.SnapshotRef, CancellationToken.None); } catch { }
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    private Task<ProcessResult> GitAsync(string path, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        processes.RunAsync(new ProcessRequest("git", arguments, path, Timeout: TimeSpan.FromSeconds(30)), cancellationToken);

    private static void Ensure(ProcessResult result)
    {
        if (result.ExitCode != 0) throw new InvalidOperationException(result.StandardError);
    }
}
