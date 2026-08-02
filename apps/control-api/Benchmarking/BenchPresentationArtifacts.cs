using System.Text.Json.Serialization;
using Comprexy.Application.Models.Benchmarking;

namespace Comprexy.ControlApi.Benchmarking;

/// <summary>
/// Read models for harness <c>presentation.json</c> and <c>manifest.json</c> artifacts.
/// Shapes mirror <see cref="Comprexy.Bench"/> records without project reference.
/// </summary>
internal sealed class BenchPresentationArtifact
{
    public string? RunId { get; set; }

    public BenchmarkCostRates? CostRates { get; set; }

    public BenchMetricsArtifact? Metrics { get; set; }

    public List<string>? TurnSeriesPaths { get; set; }
}

internal sealed class BenchMetricsArtifact
{
    public List<BenchPairedConversationArtifact>? Paired { get; set; }

    public List<BenchArmSnapshotArtifact>? Arms { get; set; }
}

internal sealed class BenchPairedConversationArtifact
{
    public string? Name { get; set; }

    public BenchConversationMetricsArtifact? MafCompact { get; set; }

    [JsonPropertyName("comprexy")]
    public BenchConversationMetricsArtifact? Comprexy { get; set; }

    public List<string>? Caveats { get; set; }
}

internal sealed class BenchConversationMetricsArtifact
{
    public Guid ConversationId { get; set; }

    public int TurnCount { get; set; }

    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    public long CompressionOverheadTokens { get; set; }

    public long? TotalProxyTurnDurationMs { get; set; }

    public long? TotalUpstreamDurationMs { get; set; }

    public long? TotalPrepareDurationMs { get; set; }

    public long ConversationWallClockMs { get; set; }
}

internal sealed class BenchArmSnapshotArtifact
{
    public string? Name { get; set; }

    public long ArmWallClockMs { get; set; }
}

internal sealed class BenchManifestArtifact
{
    public BenchmarkCostRates? CostRates { get; set; }

    public List<BenchArmManifestArtifact>? Arms { get; set; }
}

internal sealed class BenchArmManifestArtifact
{
    public string? Name { get; set; }

    public List<BenchConversationRunArtifact>? Conversations { get; set; }
}

internal sealed class BenchConversationRunArtifact
{
    public string? Name { get; set; }

    public Guid? ConversationId { get; set; }
}

internal static class BenchArmNames
{
    public const string MafCompact = "maf-compact";

    public const string Comprexy = "comprexy";
}
