using Comprexy.Application.Models.Telemetry;

namespace Comprexy.Application.Abstractions;

public interface IRegressionDetector
{
    /// <summary>
    /// Detects consecutive-turn savings drops greater than 10% relative to the earlier turn.
    /// </summary>
    IReadOnlyList<SavingsRegressionDto> DetectSavingsRegressions(
        IReadOnlyList<ConversationTurnProjection> turnsOrderedByIndex);
}
