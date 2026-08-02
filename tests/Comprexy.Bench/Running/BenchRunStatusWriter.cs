using Comprexy.Bench.Model;

namespace Comprexy.Bench.Running;

/// <summary>
/// Harness-owned in-run progress for <c>status.json</c>. Preserves orchestrator-owned outer <c>phase</c>.
/// </summary>
internal static class BenchRunStatusWriter
{
    private static readonly HashSet<string> OrchestratorPhases = new(StringComparer.Ordinal)
    {
        "queued",
        "starting",
        "running",
        "reporting",
        "completed",
        "failed",
        "cancelled",
        "completed_with_report_error"
    };

    public static async Task UpdateProgressAsync(
        string runDirectory,
        Action<BenchRunStatusDocument> mutate,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(runDirectory, "status.json");
        BenchRunStatusDocument document;
        if (File.Exists(path))
        {
            document = await BenchJson.ReadAsync<BenchRunStatusDocument>(path, cancellationToken);
        }
        else
        {
            document = new BenchRunStatusDocument();
        }

        var preservedPhase = document.Phase;
        mutate(document);

        if (preservedPhase is not null &&
            OrchestratorPhases.Contains(preservedPhase) &&
            document.Phase is not null &&
            !string.Equals(document.Phase, preservedPhase, StringComparison.Ordinal))
        {
            document.Phase = preservedPhase;
        }

        document.UpdatedAt = DateTimeOffset.UtcNow;
        await BenchJson.WriteAsync(path, document, cancellationToken);
    }

    public static Task WriteProgressAsync(
        string runDirectory,
        string? arm,
        string? conversationName,
        int? promptsCompleted,
        string runPhase,
        CancellationToken cancellationToken) =>
        UpdateProgressAsync(
            runDirectory,
            document =>
            {
                document.RunPhase = runPhase;
                document.Arm = arm;
                document.ConversationName = conversationName;
                document.PromptsCompleted = promptsCompleted;
            },
            cancellationToken);
}

internal sealed class BenchRunStatusDocument
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
}
