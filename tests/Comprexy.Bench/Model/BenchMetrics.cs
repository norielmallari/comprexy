namespace Comprexy.Bench.Model;

/// <summary>
/// Deterministic figures for one run, joined from the bench control-api. The report agent may only
/// quote numbers that appear here.
/// </summary>
internal sealed record BenchMetrics
{
    public required string RunId { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }

    public required string ComprexyCommit { get; init; }

    public required bool RepositoryDirty { get; init; }

    public required string? Model { get; init; }

    public required BenchHarnessSettings Harness { get; init; }

    public required IReadOnlyList<BenchArmSettingsSnapshot> Arms { get; init; }

    /// <summary>
    /// How every conversation ended, on both arms, whether or not it paired. An arm that stalls is
    /// a result in its own right, so it belongs in the deterministic output rather than only in an
    /// exclusion string.
    /// </summary>
    public required IReadOnlyList<BenchConversationOutcome> Outcomes { get; init; }

    public required IReadOnlyList<BenchPairedConversation> Paired { get; init; }

    /// <summary>
    /// Conversations where the baseline died in a provider/context kill zone and the treatment arm
    /// cleared that zone (status <c>survived_baseline_failure</c>). Not a full-script token pair.
    /// </summary>
    public required IReadOnlyList<BenchSurvivalConversation> Survivals { get; init; }

    public required IReadOnlyList<BenchExcludedConversation> Excluded { get; init; }

    public required BenchHeadline Headline { get; init; }
}

internal sealed record BenchArmSettingsSnapshot(
    string Name,
    string Description,
    bool ClientCompactionEnabled,
    ResolvedArmSettings Resolved,
    long ArmWallClockMs);

/// <summary>Per-arm stored metrics for one conversation.</summary>
internal sealed record BenchConversationMetrics(
    Guid ConversationId,
    int TurnCount,
    long BaselineTokensEstimated,
    long InputTokens,
    long OutputTokens,
    long NetTokensSaved,
    double NetTokenSavingsRatio,
    long CompressionOverheadTokens,
    int CompressionEventCount,
    long FinalTurnBaselineTokensEstimated,
    long FinalTurnCompressedTokensEstimated,
    /// <summary>Max per-turn tiktoken prompt tokens the arm forwarded upstream.</summary>
    long PeakPromptTokensSent,
    /// <summary>Max per-turn tiktoken raw prompt estimate (pre-compress / pre-distill view).</summary>
    long PeakRawPromptTokensEstimated,
    long? TotalProxyTurnDurationMs,
    long? TotalUpstreamDurationMs,
    long? TotalPrepareDurationMs,
    long ConversationWallClockMs,
    int? ClientCompactionCount);

/// <summary>
/// A conversation that completed on both arms with the same prompt-list hash. Savings are read
/// from the Comprexy turn ledger, not recomputed here.
/// </summary>
internal sealed record BenchPairedConversation(
    string Name,
    string PromptListHash,
    int PromptCount,
    BenchConversationMetrics MafCompact,
    BenchConversationMetrics Comprexy,
    long TokensSavedVersusBaseline,
    double TokenReductionRatio,
    IReadOnlyList<string> Caveats);

internal sealed record BenchExcludedConversation(string Name, string Reason);

/// <summary>
/// Baseline hit a provider/context kill zone; treatment cleared it under survival early-stop.
/// Peak figures are from stored turn metrics when available.
/// <see cref="CommonPrefix"/> compares tokens through the last prompt both arms fully completed
/// (baseline <c>PromptsCompleted</c> = X-1 when X is the erroring baseline prompt).
/// </summary>
internal sealed record BenchSurvivalConversation(
    string Name,
    string PromptListHash,
    int PromptCount,
    int BaselinePromptsCompleted,
    int TreatmentPromptsCompleted,
    string BaselineStatus,
    string? BaselineFailureReason,
    string? TreatmentDetail,
    BenchConversationMetrics? MafCompact,
    BenchConversationMetrics? Comprexy,
    SurvivalPrefixComparison? CommonPrefix);

/// <summary>
/// Token totals for prompts 1..CommonCompletedPrompts on both arms (the shared completed prefix
/// before the baseline's erroring prompt).
/// </summary>
internal sealed record SurvivalPrefixComparison(
    int CommonCompletedPrompts,
    int ErroringBaselinePrompt,
    long MafCompactTokensSent,
    long ComprexyTokensSent,
    long ComprexyCompressionOverheadTokens,
    long ComprexyTokensSentIncludingOverhead,
    long TokensSavedVersusBaseline,
    double TokenReductionRatio,
    long MafCompactPeakPromptTokensSent,
    long ComprexyPeakPromptTokensSent,
    int MafCompactTurnCount,
    int ComprexyTurnCount,
    /// <summary>
    /// Wall clock from the first script user message through the start of the erroring prompt
    /// (includes local tool time between proxy turns).
    /// </summary>
    long MafCompactWallClockMs,
    long ComprexyWallClockMs,
    /// <summary>Sum of per-turn <c>DurationMs</c> in the same window (proxy prepare+upstream+persist only).</summary>
    long MafCompactProxyTurnDurationMs,
    long ComprexyProxyTurnDurationMs);

internal sealed record BenchConversationOutcome(
    string Arm,
    string Name,
    string Status,
    int PromptsCompleted,
    int PromptCount,
    long ConversationWallClockMs,
    string? FailureReason);

internal sealed record BenchHeadline(
    int PairedConversationCount,
    int SurvivalConversationCount,
    int ExcludedConversationCount,
    long PairedBaselineTokensEstimated,
    long PairedComprexyTokensEstimated,
    long PairedTokensSaved,
    double PairedTokenReductionRatio,
    long? PairedMafCompactProxyTurnDurationMs,
    long? PairedComprexyProxyTurnDurationMs,
    int? ComprexyArmClientCompactionCount);
