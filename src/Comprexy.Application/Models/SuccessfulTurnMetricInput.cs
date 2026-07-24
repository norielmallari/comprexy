using Comprexy.Application.Models;

namespace Comprexy.Application.Models;

/// <summary>
/// Inputs required to persist a successful compressed-path turn metric.
/// </summary>
public sealed record SuccessfulTurnMetricInput(
    Guid ConversationId,
    string Model,
    DateTimeOffset RequestStartedAt,
    int RawInputTokensEstimated,
    int CompressedInputTokensEstimated,
    int? ActualPromptTokens,
    int? ActualCompletionTokens,
    int EstimatedCompletionTokensFallback,
    ContextBudgetDecision BudgetDecision,
    bool TrimTriggered,
    int? WorkingMemoryVersionUsed,
    int RawMessageCount,
    int SentMessageCount,
    string RequestHash,
    string SentPayloadHash);
