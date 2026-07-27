namespace Comprexy.Application.Models.Telemetry;

public sealed class ConversationSummaryDto
{
    public Guid ConversationId { get; init; }

    /// <summary>Whole-conversation turn count from the metrics summary rollup.</summary>
    public int TurnCount { get; init; }

    /// <summary>Model from the true final turn (highest <c>TurnIndex</c>).</summary>
    public string? Model { get; init; }

    public long TotalBaselineTokensEstimated { get; init; }

    public long TotalCompressedTokensEstimated { get; init; }

    public long TotalNetTokensSaved { get; init; }

    public long TotalCompressionOverheadTokens { get; init; }

    /// <summary>Whole-conversation weighted savings (rollup totals).</summary>
    public double WeightedSavingsRatio { get; init; }

    /// <summary>Whole-conversation simple average of per-turn savings ratios (EF aggregate).</summary>
    public double SimpleAverageSavingsRatio { get; init; }

    /// <summary>
    /// Median of per-turn savings ratios within the bounded <see cref="SampleTurnCount"/> window
    /// (ordered by <c>TurnIndex</c>, first N turns). Not whole-conversation when
    /// <see cref="IsPartialTurnSample"/> is true.
    /// </summary>
    public double MedianSavingsRatio { get; init; }

    /// <summary>Whole-conversation peak (max) per-turn savings ratio (EF aggregate).</summary>
    public double PeakSavingsRatio { get; init; }

    /// <summary>Savings ratio from the true final turn (highest <c>TurnIndex</c>).</summary>
    public double FinalTurnSavingsRatio { get; init; }

    /// <summary>Turns included in the bounded sample used for median and regressions.</summary>
    public int SampleTurnCount { get; init; }

    public int? SampleFirstTurnIndex { get; init; }

    public int? SampleLastTurnIndex { get; init; }

    /// <summary>True when <see cref="SampleTurnCount"/> is less than <see cref="TurnCount"/>.</summary>
    public bool IsPartialTurnSample { get; init; }

    /// <summary>
    /// Regressions detected within the bounded sample window only (see sample fields).
    /// </summary>
    public IReadOnlyList<SavingsRegressionDto> SavingsRegressions { get; init; } = [];
}

public sealed class SavingsRegressionDto
{
    public int FromTurnIndex { get; init; }

    public int ToTurnIndex { get; init; }

    public double FromSavingsRatio { get; init; }

    public double ToSavingsRatio { get; init; }

    public double RelativeDrop { get; init; }
}
