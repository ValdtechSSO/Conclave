using System.Security.Cryptography;
using System.Text;
using Conclave.Planning;

namespace Conclave.Planning.Infrastructure;

public sealed class GitRepositoryService(IProcessRunner processRunner) : IRepositorySnapshotService, IRepositoryContentReader, IRepositorySearchGuideBuilder
{

    public async Task<OriginalRepositoryState> CaptureStateAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var root = await ResolveRootAsync(repositoryPath, cancellationToken);
        var head = await GitTextAsync(root, ["rev-parse", "HEAD"], cancellationToken);
        var indexTree = await GitTextAsync(root, ["write-tree"], cancellationToken);
        var diff = await GitAsync(root, ["diff", "--binary", "HEAD"], cancellationToken);
        EnsureSuccess(diff, "capture tracked working-tree diff");
        var untracked = await GitAsync(root, ["ls-files", "--others", "--exclude-standard", "-z"], cancellationToken);
        EnsureSuccess(untracked, "enumerate untracked files");
        return new OriginalRepositoryState(head, indexTree, HashText(diff.StandardOutput), HashUntracked(root, untracked.StandardOutput));
    }

    public async Task<SharedGitState> CaptureSharedGitStateAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var root = await ResolveRootAsync(repositoryPath, cancellationToken);
        var references = await GitAsync(root, ["for-each-ref", "--format=%(refname) %(objectname)"], cancellationToken);
        EnsureSuccess(references, "capture shared references");
        var configuration = await GitAsync(root, ["config", "--local", "--null", "--list"], cancellationToken);
        EnsureSuccess(configuration, "capture local Git configuration");
        var remotes = await GitAsync(root, ["remote", "-v"], cancellationToken);
        EnsureSuccess(remotes, "capture Git remotes");
        return new SharedGitState(HashText(references.StandardOutput), HashText(configuration.StandardOutput), HashText(remotes.StandardOutput));
    }

    public async Task<RepositorySnapshot> CreateAsync(string repositoryPath, string runKey, SnapshotMode mode, CancellationToken cancellationToken)
    {
        var root = await ResolveRootAsync(repositoryPath, cancellationToken);
        var baseHead = await GitTextAsync(root, ["rev-parse", "HEAD"], cancellationToken);
        var snapshotSha = mode == SnapshotMode.Head
            ? baseHead
            : await CreateWorkingTreeCommitAsync(root, baseHead, cancellationToken);
        var safeKey = string.Concat(runKey.Where(char.IsLetterOrDigit));
        if (safeKey.Length < 8) throw new InvalidOperationException("Run key is not safe for a Git reference.");
        var snapshotRef = $"refs/conclave/runs/{safeKey}";
        var pin = await GitAsync(root, ["update-ref", snapshotRef, snapshotSha, new string('0', 40)], cancellationToken);
        EnsureSuccess(pin, "pin snapshot reference");
        return new RepositorySnapshot(runKey, root, baseHead, snapshotSha, snapshotRef, mode, mode == SnapshotMode.WorkingTree, mode == SnapshotMode.WorkingTree);
    }

    public async Task<bool> SnapshotRefMatchesAsync(RepositorySnapshot snapshot, CancellationToken cancellationToken)
    {
        var result = await GitAsync(snapshot.RepositoryPath, ["rev-parse", "--verify", snapshot.SnapshotRef], cancellationToken);
        return result.ExitCode == 0 && string.Equals(result.StandardOutput.Trim(), snapshot.SnapshotSha, StringComparison.Ordinal);
    }

    public async Task DeleteSnapshotRefAsync(string repositoryPath, string snapshotRef, CancellationToken cancellationToken)
    {
        if (!snapshotRef.StartsWith("refs/conclave/runs/", StringComparison.Ordinal))
            throw new InvalidOperationException("Ref is not owned by Conclave.");
        var root = await ResolveRootAsync(repositoryPath, cancellationToken);
        var result = await GitAsync(root, ["update-ref", "-d", snapshotRef], cancellationToken);
        EnsureSuccess(result, "delete snapshot reference");
    }

    public async Task<(bool Exists, string? Content)> ReadTextAsync(RepositorySnapshot snapshot, string repositoryRelativePath, CancellationToken cancellationToken)
    {
        if (!RepositoryPath.IsSafeRelative(repositoryRelativePath)) return (false, null);
        var spec = $"{snapshot.SnapshotSha}:{repositoryRelativePath.Replace('\\', '/')}";
        var result = await GitAsync(snapshot.RepositoryPath, ["show", spec], cancellationToken, 2_000_000);
        return result.ExitCode == 0 ? (true, result.StandardOutput) : (false, null);
    }

    public async Task<RepositorySearchGuide> BuildAsync(RepositorySnapshot snapshot, IReadOnlyList<string> suggestedRoots, RepositorySearchConfiguration limits, CancellationToken cancellationToken)
    {
        if (suggestedRoots.Count == 0) throw new ArgumentException("At least one suggested repository root is required.", nameof(suggestedRoots));
        var normalized = suggestedRoots.Select(x => x.Replace('\\', '/').TrimEnd('/')).Distinct(StringComparer.Ordinal).ToArray();
        if (normalized.Length > limits.MaxSuggestedRoots) throw new ArgumentException($"At most {limits.MaxSuggestedRoots} suggested repository roots are allowed.", nameof(suggestedRoots));
        if (normalized.Any(x => x != "." && !RepositoryPath.IsSafeRelative(x))) throw new ArgumentException("Every suggested root must be a safe repository-relative path.", nameof(suggestedRoots));

        var listArguments = new List<string> { "ls-tree", "-r", "--name-only", snapshot.SnapshotSha, "--" };
        listArguments.AddRange(normalized);
        var listed = await GitAsync(snapshot.RepositoryPath, listArguments, cancellationToken, 2_000_000);
        EnsureSuccess(listed, "validate suggested repository roots");
        var matchingFileCount = listed.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).Count();
        if (matchingFileCount == 0) throw new InvalidOperationException("The suggested repository roots contain no files in the retained snapshot.");
        return new RepositorySearchGuide(normalized, matchingFileCount);
    }

    private async Task<string> CreateWorkingTreeCommitAsync(string root, string baseHead, CancellationToken cancellationToken)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "conclave-index-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var indexPath = Path.Combine(tempDirectory, "index");
        var environment = new Dictionary<string, string?>
        {
            ["GIT_INDEX_FILE"] = indexPath,
            ["GIT_AUTHOR_NAME"] = "Conclave Snapshot",
            ["GIT_AUTHOR_EMAIL"] = "conclave@local.invalid",
            ["GIT_COMMITTER_NAME"] = "Conclave Snapshot",
            ["GIT_COMMITTER_EMAIL"] = "conclave@local.invalid"
        };
        try
        {
            EnsureSuccess(await GitAsync(root, ["read-tree", baseHead], cancellationToken, environment: environment), "initialize temporary index");
            EnsureSuccess(await GitAsync(root, ["add", "-A", "--", "."], cancellationToken, environment: environment), "capture working tree in temporary index");
            var tree = await GitTextAsync(root, ["write-tree"], cancellationToken, environment);
            return await GitTextAsync(root, ["commit-tree", tree, "-p", baseHead, "-m", "Conclave working-tree snapshot"], cancellationToken, environment);
        }
        finally
        {
            if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private async Task<string> ResolveRootAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(repositoryPath);
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException($"Repository directory does not exist: {fullPath}");
        return Path.GetFullPath(await GitTextAsync(fullPath, ["rev-parse", "--show-toplevel"], cancellationToken));
    }

    private Task<ProcessResult> GitAsync(string root, IReadOnlyList<string> arguments, CancellationToken cancellationToken, int maxOutput = 4_000_000, IReadOnlyDictionary<string, string?>? environment = null) =>
        processRunner.RunAsync(new ProcessRequest("git", arguments, root, Environment: environment, Timeout: TimeSpan.FromMinutes(2), MaxOutputCharacters: maxOutput), cancellationToken);

    private async Task<string> GitTextAsync(string root, IReadOnlyList<string> arguments, CancellationToken cancellationToken, IReadOnlyDictionary<string, string?>? environment = null)
    {
        var result = await GitAsync(root, arguments, cancellationToken, environment: environment);
        EnsureSuccess(result, $"git {string.Join(' ', arguments)}");
        return result.StandardOutput.Trim();
    }

    private static void EnsureSuccess(ProcessResult result, string operation)
    {
        if (result.ExitCode != 0) throw new InvalidOperationException($"Git failed to {operation}: {result.StandardError.Trim()}");
    }

    private static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string HashUntracked(string root, string nullSeparatedPaths)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var relative in nullSeparatedPaths.Split('\0', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal))
        {
            var normalized = relative.Replace('\\', '/');
            var path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            hash.AppendData(Encoding.UTF8.GetBytes(normalized));
            hash.AppendData([0]);
            var info = new FileInfo(path);
            if (info.LinkTarget is { } linkTarget)
                hash.AppendData(Encoding.UTF8.GetBytes("link:" + linkTarget));
            else if (File.Exists(path))
                hash.AppendData(File.ReadAllBytes(path));
            hash.AppendData([0]);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

}

public sealed class GitProviderWorkspaceService(IProcessRunner processRunner) : IProviderWorkspaceService
{
    public async Task<ProviderWorkspace> CreateAsync(RepositorySnapshot snapshot, string providerId, string path, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath) && Directory.EnumerateFileSystemEntries(fullPath).Any())
            throw new InvalidOperationException($"Workspace path is not empty: {fullPath}");
        if (Directory.Exists(fullPath)) Directory.Delete(fullPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var result = await GitAsync(snapshot.RepositoryPath, ["worktree", "add", "--detach", fullPath, snapshot.SnapshotSha], cancellationToken);
        EnsureSuccess(result, "create provider worktree");
        return new ProviderWorkspace(providerId, fullPath, snapshot.SnapshotSha);
    }

    public async Task ResetAsync(ProviderWorkspace workspace, CancellationToken cancellationToken)
    {
        EnsureSuccess(await GitAsync(workspace.Path, ["reset", "--hard", workspace.SnapshotSha], cancellationToken), "reset provider worktree");
        EnsureSuccess(await GitAsync(workspace.Path, ["clean", "-fdx"], cancellationToken), "clean provider worktree");
        var head = await GitAsync(workspace.Path, ["rev-parse", "HEAD"], cancellationToken);
        EnsureSuccess(head, "verify provider worktree");
        if (!string.Equals(head.StandardOutput.Trim(), workspace.SnapshotSha, StringComparison.Ordinal))
            throw new InvalidOperationException("Provider workspace does not resolve to the run snapshot.");
    }

    public async Task RemoveAsync(RepositorySnapshot snapshot, ProviderWorkspace workspace, CancellationToken cancellationToken)
    {
        var result = await GitAsync(snapshot.RepositoryPath, ["worktree", "remove", "--force", workspace.Path], cancellationToken);
        if (result.ExitCode != 0 && Directory.Exists(workspace.Path)) Directory.Delete(workspace.Path, recursive: true);
        await PruneMetadataAsync(snapshot.RepositoryPath, cancellationToken);
    }

    public async Task PruneMetadataAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        EnsureSuccess(await GitAsync(repositoryPath, ["worktree", "prune"], cancellationToken), "prune worktree metadata");
    }

    private Task<ProcessResult> GitAsync(string root, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        processRunner.RunAsync(new ProcessRequest("git", arguments, root, Timeout: TimeSpan.FromMinutes(2)), cancellationToken);

    private static void EnsureSuccess(ProcessResult result, string operation)
    {
        if (result.ExitCode != 0) throw new InvalidOperationException($"Git failed to {operation}: {result.StandardError.Trim()}");
    }
}
