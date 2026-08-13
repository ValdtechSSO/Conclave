using Conclave.Core;
using Conclave.Infrastructure;
using Conclave.Repository;

namespace Conclave.Repository.Tests;

public sealed class RepositoryTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "conclave-repository-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ProcessRunner _process = new();

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        await Git("init");
        await File.WriteAllTextAsync(Path.Combine(_root, "tracked.txt"), "base\n");
        await Git("add", "tracked.txt");
        await Git("-c", "user.name=Test", "-c", "user.email=test@local.invalid", "commit", "-m", "initial");
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Working_tree_snapshot_is_retained_isolated_and_resettable()
    {
        var repository = new GitRepositoryService(_process);
        var workspaces = new GitProviderWorkspaceService(_process);
        await File.WriteAllTextAsync(Path.Combine(_root, "staged.txt"), "staged");
        await Git("add", "staged.txt");
        await File.AppendAllTextAsync(Path.Combine(_root, "tracked.txt"), "unstaged\n");
        await File.WriteAllTextAsync(Path.Combine(_root, "untracked.txt"), "untracked");
        var before = await repository.CaptureStateAsync(_root, CancellationToken.None);
        var snapshot = await repository.CreateAsync(_root, Guid.NewGuid().ToString("N"), SnapshotMode.WorkingTree, CancellationToken.None);
        var first = await workspaces.CreateAsync(snapshot, "one", Path.Combine(_root, "..", "workspace-one-" + Guid.NewGuid().ToString("N")), CancellationToken.None);
        var second = await workspaces.CreateAsync(snapshot, "two", Path.Combine(_root, "..", "workspace-two-" + Guid.NewGuid().ToString("N")), CancellationToken.None);
        try
        {
            Assert.Equal("untracked", await File.ReadAllTextAsync(Path.Combine(first.Path, "untracked.txt")));
            Assert.Contains("unstaged", await File.ReadAllTextAsync(Path.Combine(second.Path, "tracked.txt")), StringComparison.Ordinal);
            await File.WriteAllTextAsync(Path.Combine(first.Path, "scratch.txt"), "only one");
            Assert.False(File.Exists(Path.Combine(second.Path, "scratch.txt")));
            await workspaces.ResetAsync(first, CancellationToken.None);
            Assert.False(File.Exists(Path.Combine(first.Path, "scratch.txt")));
            Assert.Equal(snapshot.SnapshotSha, (await GitAt(first.Path, "rev-parse", "HEAD")).StandardOutput.Trim());
        }
        finally
        {
            await workspaces.RemoveAsync(snapshot, first, CancellationToken.None);
            await workspaces.RemoveAsync(snapshot, second, CancellationToken.None);
        }
        Assert.True(await repository.SnapshotRefMatchesAsync(snapshot, CancellationToken.None));
        Assert.Equal(before, await repository.CaptureStateAsync(_root, CancellationToken.None));
        await repository.DeleteSnapshotRefAsync(_root, snapshot.SnapshotRef, CancellationToken.None);
    }

    [Fact]
    public async Task Search_guide_validates_suggested_roots_without_reading_file_contents()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src", "List"));
        Directory.CreateDirectory(Path.Combine(_root, "src", "Other"));
        await File.WriteAllTextAsync(Path.Combine(_root, "src", "List", "A.cs"), "class A { }\n");
        await File.WriteAllTextAsync(Path.Combine(_root, "src", "List", "B.cs"), new string('b', 50));
        await File.WriteAllTextAsync(Path.Combine(_root, "src", "Other", "Hidden.cs"), "class Hidden { }\n");
        await Git("add", "src");
        await Git("-c", "user.name=Test", "-c", "user.email=test@local.invalid", "commit", "-m", "context fixture");
        var repository = new GitRepositoryService(_process);
        var snapshot = await repository.CreateAsync(_root, Guid.NewGuid().ToString("N"), SnapshotMode.Head, CancellationToken.None);

        var guide = await repository.BuildAsync(snapshot, ["src/List"], new RepositorySearchConfiguration { MaxSuggestedRoots = 2 }, CancellationToken.None);

        Assert.Equal(["src/List"], guide.SuggestedRoots);
        Assert.Equal(2, guide.MatchingFileCount);
        await repository.DeleteSnapshotRefAsync(_root, snapshot.SnapshotRef, CancellationToken.None);
    }

    private async Task Git(params string[] arguments)
    {
        var result = await GitAt(_root, arguments);
        Assert.True(result.ExitCode == 0, result.StandardError);
    }

    private Task<ProcessResult> GitAt(string path, params string[] arguments) => _process.RunAsync(new ProcessRequest("git", arguments, path), CancellationToken.None);
}
