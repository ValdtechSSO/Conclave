using System.Diagnostics;
using System.Text;
using Conclave.Core;

namespace Conclave.Infrastructure;

public sealed class ProcessRunner : IProcessRunner
{
    private static readonly string[] SensitiveFragments = ["TOKEN", "SECRET", "PASSWORD", "API_KEY", "APIKEY", "CREDENTIAL"];

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardInput = request.StandardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in request.Arguments)
            startInfo.ArgumentList.Add(argument);

        foreach (var key in startInfo.Environment.Keys.Where(IsSensitiveEnvironmentName).ToArray())
            startInfo.Environment.Remove(key);

        if (request.Environment is not null)
        {
            foreach (var pair in request.Environment)
            {
                if (pair.Value is null) startInfo.Environment.Remove(pair.Key);
                else startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        if (!process.Start())
            throw new InvalidOperationException($"Could not start process '{request.FileName}'.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeout = request.Timeout is { } duration ? new CancellationTokenSource(duration) : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var timedOut = false;
        var cancelled = false;
        try
        {
            if (request.StandardInput is not null)
            {
                await process.StandardInput.WriteAsync(request.StandardInput.AsMemory(), linked.Token);
                await process.StandardInput.FlushAsync(linked.Token);
                process.StandardInput.Close();
            }
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            cancelled = cancellationToken.IsCancellationRequested;
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None);
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        stopwatch.Stop();
        var outputTruncated = stdout.Length > request.MaxOutputCharacters || stderr.Length > request.MaxOutputCharacters;
        return new ProcessResult(
            process.ExitCode,
            Truncate(stdout, request.MaxOutputCharacters),
            Truncate(stderr, request.MaxOutputCharacters),
            stopwatch.Elapsed,
            timedOut,
            cancelled,
            outputTruncated);
    }

    private static bool IsSensitiveEnvironmentName(string name) =>
        SensitiveFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static string Truncate(string value, int limit) => value.Length <= limit ? value : value[..limit] + "\n[output truncated]";

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
    }
}
