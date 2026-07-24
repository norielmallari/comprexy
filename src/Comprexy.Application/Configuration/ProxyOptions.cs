namespace Comprexy.Application.Configuration;

/// <summary>
/// Proxy-level behavior toggles that sit above provider and context-policy configuration.
/// </summary>
public class ProxyOptions
{
    public const string SectionName = "Proxy";

    /// <summary>
    /// When true, Comprexy forwards the client's request body to the upstream provider with all
    /// fields preserved (only rewriting model/stream as required). Context rebuild, soft/hard
    /// compression, and hard-limit 413 enforcement are skipped. Conversation persistence still
    /// occurs for diagnostics. Escape hatch — leave false for normal budgeted proxy use.
    /// </summary>
    public bool PassThrough { get; set; }

    /// <summary>
    /// When true, strip assistant reasoning fields (<c>reasoning_content</c>,
    /// <c>reasoning</c>) from messages before sending them to chat or compression models.
    /// Persisted wire JSON is unchanged. Default is false (forward reasoning fields as sent).
    /// </summary>
    public bool StripReasoningContent { get; set; }
}
