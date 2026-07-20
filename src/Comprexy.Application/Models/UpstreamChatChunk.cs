namespace Comprexy.Application.Models;

/// <summary>
/// A normalized delta received from an OpenAI-compatible streaming response.
/// </summary>
public sealed record UpstreamChatChunk(
    string? Role,
    string? Content,
    string? FinishReason,
    int? PromptTokens = null,
    int? CompletionTokens = null);
