namespace Comprexy.Application.Configuration;

/// <summary>
/// Governs how Comprexy estimates and budgets context tokens and Inline fold retain windows.
/// </summary>
public class ContextPolicyOptions
{
    public const string SectionName = "ContextPolicy";

    /// <summary>
    /// Above this, eligible turns run a blocking Inline wrap-up after the visible answer.
    /// Below this, no wrap-up eligibility.
    /// </summary>
    public int SoftLimitTokens { get; set; } = 40_000;

    /// <summary>
    /// Minimum client-visible assistant turns after a successful Inline generation before
    /// another follow-up wrap-up is allowed.
    /// </summary>
    public int MinTurnsBetweenGenerations { get; set; } = 6;

    /// <summary>
    /// Number of trailing unfolded messages kept raw when Inline wrap-up folds (atomic
    /// assistant+tool groups count as one window unit via <see cref="Services.RecentContextSelector"/>).
    /// Default 1 keeps only the tip (and its tool group if applicable), folding the rest into
    /// working memory.
    /// </summary>
    public int CompressionRetainMessageCount { get; set; } = 1;

    /// <summary>
    /// When true (default), drop older identical failed file-edit tool results (path +
    /// <c>old_string</c> last-wins) from the live chat outgoing retain window so StrReplace
    /// failure loops do not stack. Wire-only (never marks folded), and applied while the retain
    /// window is built — under Cache Alignment the omit is baked into the frozen Prefix rather
    /// than re-applied per turn.
    /// </summary>
    public bool DedupeDuplicateFailedEdits { get; set; } = true;

    /// <summary>Tiktoken encoding used for approximate token estimation.</summary>
    public string TokenizerEncoding { get; set; } = "cl100k_base";
}
