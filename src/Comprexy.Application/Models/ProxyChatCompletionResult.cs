namespace Comprexy.Application.Models;

/// <summary>
/// Result returned by <see cref="Services.ProxyChatCompletionService"/> to the Api layer.
/// When <see cref="RawResponseJson"/> is set, the API should return that body as-is.
/// </summary>
public sealed record ProxyChatCompletionResult(
    Guid ConversationId,
    string AssistantContent,
    string? FinishReason,
    int PromptTokens,
    int CompletionTokens,
    string Model,
    int EstimatedTokens,
    ContextBudgetDecision BudgetDecision,
    bool CompressionSkipped,
    string? RawResponseJson = null);
