namespace Comprexy.Application.Models.Telemetry;

/// <summary>
/// Read-side split of a turn's prepared prompt (<c>CompressedInputTokensEstimated</c>) into the
/// parts an operator can act on. Derived at query time from the captured system prompt and the
/// working-memory version the turn used — the turn row itself stores no breakdown.
/// The three values sum to the prepared prompt estimate.
/// </summary>
public sealed class ConversationTurnContextBreakdown
{
    public int TurnIndex { get; init; }

    /// <summary>
    /// Estimate of the conversation's captured system prompt. Constant across turns because
    /// the first-turn system prompt is reused for every rebuild.
    /// </summary>
    public int SystemPromptTokensEstimated { get; init; }

    /// <summary>
    /// Stored token count of the working-memory version this turn used; zero before the first
    /// successful compression (<c>WorkingMemoryVersionUsed</c> is null).
    /// </summary>
    public int WorkingMemoryTokensEstimated { get; init; }

    /// <summary>
    /// Remainder of the prepared prompt: still-unfolded raw turns plus the model-facing tool
    /// catalog and message framing, which the prompt estimate also covers.
    /// </summary>
    public int HistoryAndToolsTokensEstimated { get; init; }
}
