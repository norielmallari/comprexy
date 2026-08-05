namespace Comprexy.Domain.Entities;

/// <summary>
/// Per-turn token accounting for a successful compressed-path chat completion.
/// SoftBudget savings compare tiktoken <see cref="IrFullInputTokensEstimated"/> (IR tools +
/// full unfolded IR transcript) vs <see cref="CompressedInputTokensEstimated"/> (prepared with
/// WM/retain) when IrFull is present. Legacy rows with null IrFull keep NativeRaw vs Prepared.
/// <see cref="VirtualToolsTokensSaved"/> is NativeRaw − IrFull when IrFull is present (can be
/// negative). <see cref="ActualPromptTokens"/> is retained for estimate-accuracy reporting and
/// is not used in <see cref="NetTokensSaved"/>.
/// </summary>
public class ConversationTurnMetric : EntityBase
{
    public Guid ConversationId { get; private set; }

    public int TurnIndex { get; private set; }

    public DateTimeOffset RequestStartedAt { get; private set; }

    public string Model { get; private set; } = string.Empty;

    public int RawInputTokensEstimated { get; private set; }

    /// <summary>
    /// IR tools + full unfolded IR transcript (no WM fold), when captured at prepare.
    /// Null on legacy / pre-migration rows (mixed-axis SoftBudget).
    /// </summary>
    public int? IrFullInputTokensEstimated { get; private set; }

    public int CompressedInputTokensEstimated { get; private set; }

    public int? ActualPromptTokens { get; private set; }

    public int ActualCompletionTokens { get; private set; }

    public int BaselineTotalTokensEstimated { get; private set; }

    public int CompressedTotalTokensEstimated { get; private set; }

    public int NetTokensSaved { get; private set; }

    public double NetTokenSavingsRatio { get; private set; }

    /// <summary>
    /// NativeRaw − IrFull when IrFull is present; null on legacy rows. May be negative when
    /// IR history tax exceeds native-wire catalog savings.
    /// </summary>
    public int? VirtualToolsTokensSaved { get; private set; }

    public bool SoftBudgetExceeded { get; private set; }

    public bool HardBudgetExceeded { get; private set; }

    public bool TrimTriggered { get; private set; }

    public int? WorkingMemoryVersionUsed { get; private set; }

    public int RawMessageCount { get; private set; }

    public int SentMessageCount { get; private set; }

    public string RequestHash { get; private set; } = string.Empty;

    public string SentPayloadHash { get; private set; } = string.Empty;

    /// <summary>
    /// Proxy turn wall clock from accept through the metric write (prepare + upstream + persist).
    /// Excludes Inline wrap-up, which is timed separately on <c>CompressionEvent.DurationMs</c>.
    /// Null on rows written before turn timing was captured.
    /// </summary>
    public int? DurationMs { get; private set; }

    /// <summary>
    /// Time blocked on the provider HTTP call (including SSE forwarding on streaming turns).
    /// </summary>
    public int? UpstreamDurationMs { get; private set; }

    /// <summary>
    /// Prepare-path work before the upstream call: rebuild, ToolSchema, budget evaluation.
    /// </summary>
    public int? PrepareDurationMs { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private ConversationTurnMetric()
    {
    }

    public static ConversationTurnMetric Create(
        Guid conversationId,
        int turnIndex,
        DateTimeOffset requestStartedAt,
        string model,
        int rawInputTokensEstimated,
        int compressedInputTokensEstimated,
        int? actualPromptTokens,
        int actualCompletionTokens,
        bool softBudgetExceeded,
        bool hardBudgetExceeded,
        bool trimTriggered,
        int? workingMemoryVersionUsed,
        int rawMessageCount,
        int sentMessageCount,
        string requestHash,
        string sentPayloadHash,
        int? durationMs,
        int? upstreamDurationMs,
        int? prepareDurationMs,
        DateTimeOffset createdAt,
        int? irFullInputTokensEstimated = null)
    {
        // SoftBudget: IrFull vs Prepared when IrFull present; legacy null IrFull keeps NativeRaw vs Prepared.
        // ActualPromptTokens is accuracy-only — never enters persisted NetTokensSaved.
        var softBudgetBaselineInput = irFullInputTokensEstimated ?? rawInputTokensEstimated;
        var baselineTotal = softBudgetBaselineInput + actualCompletionTokens;
        var compressedTotal = compressedInputTokensEstimated + actualCompletionTokens;
        var netSaved = baselineTotal - compressedTotal;
        var ratio = baselineTotal > 0
            ? Math.Round((double)netSaved / baselineTotal, 6)
            : 0d;
        int? virtualToolsTokensSaved = irFullInputTokensEstimated is int irFull
            ? rawInputTokensEstimated - irFull
            : null;

        return new ConversationTurnMetric
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            TurnIndex = turnIndex,
            RequestStartedAt = requestStartedAt,
            Model = model,
            RawInputTokensEstimated = rawInputTokensEstimated,
            IrFullInputTokensEstimated = irFullInputTokensEstimated,
            CompressedInputTokensEstimated = compressedInputTokensEstimated,
            ActualPromptTokens = actualPromptTokens,
            ActualCompletionTokens = actualCompletionTokens,
            BaselineTotalTokensEstimated = baselineTotal,
            CompressedTotalTokensEstimated = compressedTotal,
            NetTokensSaved = netSaved,
            NetTokenSavingsRatio = ratio,
            VirtualToolsTokensSaved = virtualToolsTokensSaved,
            SoftBudgetExceeded = softBudgetExceeded,
            HardBudgetExceeded = hardBudgetExceeded,
            TrimTriggered = trimTriggered,
            WorkingMemoryVersionUsed = workingMemoryVersionUsed,
            RawMessageCount = rawMessageCount,
            SentMessageCount = sentMessageCount,
            RequestHash = requestHash,
            SentPayloadHash = sentPayloadHash,
            DurationMs = durationMs,
            UpstreamDurationMs = upstreamDurationMs,
            PrepareDurationMs = prepareDurationMs,
            CreatedAt = createdAt
        };
    }
}
