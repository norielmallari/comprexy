namespace Comprexy.Application.Configuration;

/// <summary>
/// Process-local wrap-up-ready message Prefix ownership for provider KV / prompt-cache alignment.
/// </summary>
public class CacheAlignmentOptions
{
    public const string SectionName = "CacheAlignment";

    /// <summary>Default max conversations retained in the process-local Prefix store.</summary>
    public const int DefaultMaxConversations = 1024;

    /// <summary>
    /// When true, prepare/wrap-up use Cache Alignment for model-facing messages.
    /// When false, prepare keeps <see cref="Services.ContextBuilder.Build"/> (legacy every-turn rebuild).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Max conversations in the process-local Prefix map (entry weight = 1). Evicts LRU when over cap.
    /// </summary>
    public int MaxConversations { get; set; } = DefaultMaxConversations;
}
