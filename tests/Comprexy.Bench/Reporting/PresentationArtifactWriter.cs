using Comprexy.Bench.Model;

namespace Comprexy.Bench.Reporting;

/// <summary>
/// Materializes dashboard-facing presentation artifacts from joined metrics and control-api turns.
/// </summary>
internal static class PresentationArtifactWriter
{
    public static async Task WriteAsync(
        string runDirectory,
        BenchManifest manifest,
        BenchMetrics metrics,
        ControlApiClient controlApi,
        CancellationToken cancellationToken)
    {
        var turnSeriesPaths = new List<string>();
        foreach (var arm in manifest.Arms)
        {
            foreach (var conversation in arm.Conversations)
            {
                if (conversation.ConversationId is not { } conversationId)
                {
                    continue;
                }

                var turns = await controlApi.GetTurnsAsync(conversationId, cancellationToken);
                if (turns.Count == 0)
                {
                    continue;
                }

                var fileName = $"turns-{arm.Name}-{conversation.Name}.json";
                var path = Path.Combine(runDirectory, fileName);
                await BenchJson.WriteAsync(path, turns, cancellationToken);
                turnSeriesPaths.Add(fileName);
            }
        }

        var presentation = new BenchPresentationFile
        {
            RunId = metrics.RunId,
            GeneratedAt = metrics.GeneratedAt,
            CostRates = manifest.CostRates,
            Metrics = metrics,
            TurnSeriesPaths = turnSeriesPaths
        };

        await BenchJson.WriteAsync(Path.Combine(runDirectory, "presentation.json"), presentation, cancellationToken);
    }
}

internal sealed record BenchPresentationFile
{
    public required string RunId { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }

    public Comprexy.Application.Models.Benchmarking.BenchmarkCostRates? CostRates { get; init; }

    public required BenchMetrics Metrics { get; init; }

    public required IReadOnlyList<string> TurnSeriesPaths { get; init; }
}
