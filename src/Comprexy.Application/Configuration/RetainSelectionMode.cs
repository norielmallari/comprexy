namespace Comprexy.Application.Configuration;

/// <summary>
/// How soft compression chooses which raw messages stay unfolded after a successful job.
/// </summary>
public enum RetainSelectionMode
{
    /// <summary>Keep a trailing Fixed tip window (<see cref="ContextPolicyOptions.CompressionRetainMessageCount"/>, default tip-only).</summary>
    Fixed = 0,

    /// <summary>Ask the compression model which sequences to keep unfolded (JSON retain list).</summary>
    Smart = 1,

    /// <summary>
    /// Live chat model produces working memory via a blocking proxy-internal follow-up wrap-up
    /// on eligible soft-pressure turns; background Fixed/Smart compression and emergency sync
    /// are disabled.
    /// </summary>
    Inline = 2
}
