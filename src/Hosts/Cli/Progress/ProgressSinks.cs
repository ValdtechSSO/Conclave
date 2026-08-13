using System.Text.Json;
using Conclave.Planning;

namespace Conclave.Cli;

internal sealed class ConsoleProgressSink(bool jsonLines) : IConclaveProgressSink
{
    private static readonly JsonSerializerOptions JsonlOptions = new(ConclaveJson.Options) { WriteIndented = false };
    private readonly object _gate = new();

    public void Report(ConclaveProgressUpdate update)
    {
        lock (_gate)
        {
            Console.Error.WriteLine(jsonLines ? JsonSerializer.Serialize(update, JsonlOptions) : Text(update));
            Console.Error.Flush();
        }
    }

    private static string Text(ConclaveProgressUpdate update)
    {
        var provider = update.Provider is null ? "" : $"/{update.Provider}";
        var elapsed = update.ElapsedSeconds is null ? "" : $" ({FormatElapsed(update.ElapsedSeconds.Value)})";
        return $"[conclave] {update.Phase}{provider}: {update.Status.ToString().ToLowerInvariant()} — {update.Message}{elapsed}";
    }

    private static string FormatElapsed(double seconds)
    {
        var elapsed = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return elapsed.TotalHours >= 1 ? $"elapsed {elapsed:hh\\:mm\\:ss}" : $"elapsed {elapsed:mm\\:ss}";
    }
}

internal sealed class JsonlFileProgressSink(string path) : IConclaveProgressSink
{
    private static readonly JsonSerializerOptions JsonlOptions = new(ConclaveJson.Options) { WriteIndented = false };
    private readonly object _gate = new();

    public void Report(ConclaveProgressUpdate update)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, JsonSerializer.Serialize(update, JsonlOptions) + Environment.NewLine);
        }
    }
}

internal sealed class CompositeProgressSink(params IConclaveProgressSink[] sinks) : IConclaveProgressSink
{
    public void Report(ConclaveProgressUpdate update)
    {
        foreach (var sink in sinks) sink.Report(update);
    }
}
