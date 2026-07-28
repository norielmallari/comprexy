namespace Comprexy.Domain.Enums;

/// <summary>
/// How working memory was produced for a compression event.
/// </summary>
public enum CompressionMode
{
    /// <summary>Working memory produced by a proxy-internal follow-up wrap-up on an eligible soft-pressure turn.</summary>
    Inline = 0
}
