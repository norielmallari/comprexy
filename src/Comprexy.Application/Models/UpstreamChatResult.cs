namespace Comprexy.Application.Models;

/// <summary>
/// Result of a single call to an upstream OpenAI-compatible chat completions endpoint.
/// </summary>
public sealed record UpstreamChatResult(
    string Content,
    string? FinishReason,
    int? PromptTokens,
    int? CompletionTokens,
    string? RawResponseJson = null,
    /// <summary>
    /// The assistant <c>message</c> object to persist (role/content/tool_calls). Required for
    /// tool-call turns where <see cref="Content"/> is empty.
    /// </summary>
    string? AssistantMessageJson = null);
