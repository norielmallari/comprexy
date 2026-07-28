namespace Comprexy.Application.Configuration;

/// <summary>
/// Governs how Comprexy estimates and budgets context tokens and compression retain windows.
/// Soft/hard limits are explicit; they are not derived from a separate model-window formula.
/// </summary>
public class ContextPolicyOptions
{
    public const string SectionName = "ContextPolicy";

    /// <summary>Above this, high-priority background compression is enqueued after the response. Below this, no compression is queued.</summary>
    public int SoftLimitTokens { get; set; } = 40_000;

    /// <summary>
    /// Chat prepare hard-budget behavior. Default <see cref="EmergencyCompressionMode.Off"/> avoids
    /// blocking the user on a sync compression call; over-hard requests trim then 413.
    /// Set <see cref="EmergencyCompressionMode.Sync"/> to restore blocking emergency compression.
    /// </summary>
    public EmergencyCompressionMode EmergencyCompression { get; set; } = EmergencyCompressionMode.Off;

    /// <summary>
    /// When true, an arriving chat request cancels in-flight soft background compression for the
    /// same conversation. When false (default), chat waits for soft compression to finish.
    /// </summary>
    public bool CancelBackgroundCompressionOnChat { get; set; } = false;

    /// <summary>
    /// At or above this: when <see cref="EmergencyCompression"/> is <see cref="EmergencyCompressionMode.Sync"/>,
    /// run synchronous emergency compression before forwarding; otherwise send-time trim then 413 if still over.
    /// Set to <c>null</c> to disable hard limit checking.
    /// </summary>
    public int? HardLimitTokens { get; set; } = 64_000;

    /// <summary>
    /// Max tokens allowed in a compression-model prompt body (full raw transcript or WM + fold
    /// segment). Soft compression prefers full raw when total stored message tokens are at or
    /// below this; otherwise it merges into working memory. Set to <c>null</c> to disable the cap
    /// (soft compression then always prefers full-raw rebuild).
    /// </summary>
    public int? CompressionMaxInputTokens { get; set; } = 65_536;

    /// <summary>
    /// Soft compression retain strategy. Default <see cref="RetainSelectionMode.Inline"/>.
    /// <see cref="RetainSelectionMode.Smart"/> is soft-only (never emergency/hard).
    /// Smart reuses the live chat message prefix plus a trailing retain-index instruction.
    /// <see cref="RetainSelectionMode.Inline"/> disables background soft compression and
    /// <see cref="EmergencyCompression"/> sync; after an eligible soft-pressure visible answer,
    /// a blocking proxy-internal wrap-up call produces working memory.
    /// </summary>
    public RetainSelectionMode RetainSelection { get; set; } = RetainSelectionMode.Inline;

    /// <summary>
    /// Inline retain only: minimum client-visible assistant turns after a successful Inline
    /// generation before another follow-up wrap-up is allowed. Ignored by Fixed and Smart.
    /// </summary>
    public int MinTurnsBetweenGenerations { get; set; } = 6;

    /// <summary>
    /// Number of trailing unfolded messages kept raw when Fixed compression runs (atomic
    /// assistant+tool groups count as one window unit via <see cref="RecentContextSelector"/>).
    /// Default 1 keeps only the tip (and its tool group if applicable), folding the rest into
    /// working memory. Also used as Smart FixedFallback.
    /// </summary>
    public int CompressionRetainMessageCount { get; set; } = 1;

    /// <summary>
    /// Trailing raw messages kept for emergency Fixed compression. Default 1 matches soft Fixed
    /// (fold the whole conversation except the tip / tip tool group).
    /// </summary>
    public int EmergencyRecentMessageCount { get; set; } = 1;

    /// <summary>
    /// Token budget for the Fixed compression retain window (newest-first). Also used for Smart
    /// FixedFallback. Not applied as the Smart success-path clamp (see SmartRetain*).
    /// </summary>
    public int MaxRecentRawTokens { get; set; } = 24_000;

    /// <summary>
    /// Smart retain only: max messages kept unfolded after validation/clamp.
    /// </summary>
    public int SmartRetainMaxMessages { get; set; } = 8;

    /// <summary>
    /// Smart retain only: max tokens across retained messages after clamp.
    /// </summary>
    public int SmartRetainMaxTokens { get; set; } = 24_000;

    /// <summary>
    /// When true (default), drop older duplicate file tool reads (path-keyed last-wins):
    /// (1) soft compression — from the compression prompt corpus before the LLM call, shrinking
    /// the Fixed tip; dropped copies fold on success; (2) live chat prepare — wire-only omit
    /// from the outgoing retain window (does not mark folded). Does not rewrite Smart retain
    /// after the model responds. Emergency compression never runs the soft pre-LLM pass.
    /// </summary>
    public bool DedupeDuplicateFileReads { get; set; } = true;

    /// <summary>Tiktoken encoding used for approximate token estimation.</summary>
    public string TokenizerEncoding { get; set; } = "cl100k_base";
}
