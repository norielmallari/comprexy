using System.Text.Json;
using System.Text.Json.Serialization;

namespace Comprexy.ControlApi.Benchmarking;

public static class BenchOuterPhases
{
    public const string Queued = "queued";
    public const string Starting = "starting";
    public const string Running = "running";
    public const string Reporting = "reporting";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string CompletedWithReportError = "completed_with_report_error";

    public static bool IsTerminal(string phase) =>
        phase is Completed or Failed or Cancelled or CompletedWithReportError;
}

public sealed class BenchStatusDocument
{
    public string? RunId { get; set; }

    public string? Phase { get; set; }

    public string? RunPhase { get; set; }

    public string? Arm { get; set; }

    public string? ConversationName { get; set; }

    public int? PromptsCompleted { get; set; }

    public int? PromptCount { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public List<string>? ConversationNames { get; set; }
}

public sealed class BenchIndexDocument
{
    public List<BenchIndexEntry> Runs { get; set; } = [];
}

public sealed class BenchIndexEntry
{
    public required string RunId { get; set; }

    public required string Phase { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public List<string> ConversationNames { get; set; } = [];

    public string? ModelKind { get; set; }
}

public static class BenchArtifactStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task WriteStatusAsync(string path, BenchStatusDocument status, CancellationToken cancellationToken)
    {
        status.UpdatedAt = DateTimeOffset.UtcNow;
        await WriteAtomicAsync(path, status, cancellationToken);
    }

    public static async Task<BenchStatusDocument?> ReadStatusAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<BenchStatusDocument>(stream, JsonOptions, cancellationToken);
    }

    public static async Task WriteIndexAsync(string path, BenchIndexDocument index, CancellationToken cancellationToken)
    {
        await WriteAtomicAsync(path, index, cancellationToken);
    }

    public static async Task<BenchIndexDocument> ReadOrCreateIndexAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new BenchIndexDocument();
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<BenchIndexDocument>(stream, JsonOptions, cancellationToken)
            ?? new BenchIndexDocument();
    }

    private static async Task WriteAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(value, JsonOptions), cancellationToken);
        File.Move(temp, path, overwrite: true);
    }
}
