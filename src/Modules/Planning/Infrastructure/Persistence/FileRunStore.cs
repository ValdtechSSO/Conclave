using System.Text.Json;
using Conclave.Planning;

namespace Conclave.Planning.Infrastructure;

public sealed class FileRunStore(string homePath) : IRunStore
{
    private readonly string _runsPath = Path.Combine(Path.GetFullPath(homePath), "runs");

    public string GetRunPath(string runId) => Path.Combine(_runsPath, SanitizeRunId(runId));

    public Task InitializeAsync(string runId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = GetRunPath(runId);
        foreach (var child in new[] { "request", "private", "workspaces", "proposals", "validation", "reviews", "synthesis", "logs" })
            Directory.CreateDirectory(Path.Combine(root, child));
        return Task.CompletedTask;
    }

    public Task WriteJsonAsync<T>(string runId, string relativePath, T value, CancellationToken cancellationToken) =>
        WriteTextAsync(runId, relativePath, JsonSerializer.Serialize(value, ConclaveJson.Options), cancellationToken);

    public async Task WriteTextAsync(string runId, string relativePath, string value, CancellationToken cancellationToken)
    {
        var path = Resolve(runId, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(temporary, value, cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    public async Task<T?> ReadJsonAsync<T>(string runId, string relativePath, CancellationToken cancellationToken)
    {
        var path = Resolve(runId, relativePath);
        if (!File.Exists(path)) return default;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, ConclaveJson.Options, cancellationToken);
    }

    public Task<IReadOnlyList<string>> ListRunIdsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(_runsPath)) return Task.FromResult<IReadOnlyList<string>>([]);
        IReadOnlyList<string> values = Directory.GetDirectories(_runsPath).Select(Path.GetFileName).Where(x => x is not null).Cast<string>().Order().ToArray();
        return Task.FromResult(values);
    }

    private string Resolve(string runId, string relativePath)
    {
        if (Path.IsPathRooted(relativePath)) throw new ArgumentException("Artifact path must be relative.", nameof(relativePath));
        var root = GetRunPath(runId);
        var result = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!result.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !string.Equals(result, root, StringComparison.Ordinal))
            throw new ArgumentException("Artifact path escapes the run directory.", nameof(relativePath));
        return result;
    }

    public static string SanitizeRunId(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId)) throw new ArgumentException("Run ID is required.", nameof(runId));
        var sanitized = string.Concat(runId.Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-'));
        if (sanitized.Length > 100) sanitized = sanitized[..100];
        if (string.IsNullOrWhiteSpace(sanitized)) throw new ArgumentException("Run ID has no usable characters.", nameof(runId));
        return sanitized;
    }
}
