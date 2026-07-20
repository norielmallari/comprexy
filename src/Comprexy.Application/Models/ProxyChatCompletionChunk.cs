namespace Comprexy.Application.Models;

/// <summary>
/// A streaming chat-completion delta enriched with Comprexy conversation metadata.
/// </summary>
public sealed record ProxyChatCompletionChunk(
    Guid ConversationId,
    string Model,
    string? Role,
    string? Content,
    string? FinishReason);
