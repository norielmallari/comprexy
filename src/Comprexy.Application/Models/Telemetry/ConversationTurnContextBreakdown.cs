namespace Comprexy.Application.Models.Telemetry;

/// <summary>
/// Read-side split of a turn's prepared prompt into operator-actionable segments.
/// System and working-memory segments are derived at query time; catalog and rules
/// estimates are persisted on the turn row at prepare. History is the residual so the
/// named segments plus history sum to the prepared basis (Estimated or ProviderActual).
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
    /// Persisted prepare estimate of Virtual IR tool defs plus conversation-id meta on the
    /// model-facing catalog. Zero when VT was off / N/A.
    /// </summary>
    public int PreparedVirtualToolSchemaTokensEstimated { get; init; }

    /// <summary>
    /// Persisted prepare estimate of client passthrough tool defs (or wire tools/functions
    /// when VT rewrite did not run).
    /// </summary>
    public int PreparedClientToolSchemaTokensEstimated { get; init; }

    /// <summary>
    /// Persisted prepare estimate of ephemeral pending rule messages. Zero when none pending.
    /// </summary>
    public int PreparedRulesTokensEstimated { get; init; }

    /// <summary>
    /// Residual of the prepared basis after system, WM, catalog, and rules segments.
    /// Absorbs array framing, <c>tool_choice</c> / <c>response_format</c>, and any
    /// ProviderActual gap — named segments are not scaled.
    /// </summary>
    public int HistoryTokensEstimated { get; init; }
}
