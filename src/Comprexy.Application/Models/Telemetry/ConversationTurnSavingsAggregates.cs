namespace Comprexy.Application.Models.Telemetry;

/// <summary>
/// Whole-conversation EF aggregates over turn savings ratios (not bounded-window).
/// </summary>
public sealed class ConversationTurnSavingsAggregates
{
    public double PeakNetTokenSavingsRatio { get; init; }

    public double SimpleAverageNetTokenSavingsRatio { get; init; }

    public int TurnCount { get; init; }
}
