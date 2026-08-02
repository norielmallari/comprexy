using Comprexy.Application.Models.Benchmarking;

namespace Comprexy.ControlApi.Contracts.Benchmark;

public sealed class BenchmarkTelemetryPresentationRequest
{
    public required Guid ConversationId { get; init; }

    public BenchmarkCostRates? Rates { get; init; }

    public BenchmarkModelKind ModelKind { get; init; } = BenchmarkModelKind.Local;
}

public sealed class BenchmarkComparisonPresentationRequest
{
    public required Guid BaselineConversationId { get; init; }

    public required Guid CompareConversationId { get; init; }

    public BenchmarkCostRates? Rates { get; init; }

    public BenchmarkModelKind ModelKind { get; init; } = BenchmarkModelKind.Local;
}

public sealed class BenchmarkTelemetryPresentationResponse
{
    public required ConversationTokenTotals Totals { get; init; }

    public BenchmarkCostBreakdown? Cost { get; init; }
}

public sealed class BenchmarkComparisonPresentationResponse
{
    public required BenchmarkComparisonTotals Totals { get; init; }

    public BenchmarkCostBreakdown? Cost { get; init; }

    /// <summary>Primary baseline conversation id for UI auto-fill (first paired script).</summary>
    public Guid? BaselineConversationId { get; init; }

    /// <summary>Primary compare conversation id for UI auto-fill (first paired script).</summary>
    public Guid? CompareConversationId { get; init; }

    /// <summary>Bench run id when presentation is file-backed.</summary>
    public string? RunId { get; init; }

    public IReadOnlyList<string> TurnSeriesPaths { get; init; } = [];
}

public sealed class BenchmarkScenarioDto
{
    public required string Name { get; init; }

    public required int PromptCount { get; init; }

    public string? Description { get; init; }

    public bool IsSmoke { get; init; }
}

public sealed class BenchmarkStartRunRequest
{
    public IReadOnlyList<string> Conversations { get; init; } = [];

    public BenchmarkCostRates? Rates { get; init; }

    public BenchmarkModelKind ModelKind { get; init; } = BenchmarkModelKind.Local;

    public string? RunLabel { get; init; }
}

public sealed class BenchmarkStartRunResponse
{
    public required string RunId { get; init; }
}

public sealed record BenchmarkRunSummaryDto
{
    public required string RunId { get; init; }

    public required string Phase { get; init; }

    public string? RunPhase { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    public string? LastError { get; init; }

    public string? Arm { get; init; }

    public string? ConversationName { get; init; }

    public int? PromptsCompleted { get; init; }

    public int? PromptCount { get; init; }

    public IReadOnlyList<string> ConversationNames { get; init; } = [];

    public BenchmarkCostRates? CostRates { get; init; }
}

public sealed class BenchmarkRunArtifactsDto
{
    public required string RunId { get; init; }

    public string? ManifestPath { get; init; }

    public string? MetricsPath { get; init; }

    public string? SummaryPath { get; init; }

    public string? PresentationPath { get; init; }

    public IReadOnlyList<string> TurnSeriesPaths { get; init; } = [];
}
