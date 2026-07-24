using Comprexy.Domain.Enums;

namespace Comprexy.Domain.Entities;

/// <summary>
/// Diagnostic record of a single compression attempt, used for observability and troubleshooting.
/// </summary>
public class CompressionEvent : EntityBase
{
    public Guid ConversationId { get; private set; }

    public CompressionMode Mode { get; private set; }

    public CompressionStatus Status { get; private set; }

    public int OriginalTokens { get; private set; }

    public int? CompressedTokens { get; private set; }

    public int? WorkingMemoryVersionBefore { get; private set; }

    public int? WorkingMemoryVersionAfter { get; private set; }

    public int FoldedMessageCount { get; private set; }

    public long DurationMs { get; private set; }

    public string? ErrorMessage { get; private set; }

    /// <summary>Provider or estimated prompt tokens for the compression LLM call.</summary>
    public int? PromptTokens { get; private set; }

    /// <summary>Provider or estimated completion tokens for the compression LLM call.</summary>
    public int? CompletionTokens { get; private set; }

    /// <summary>Prompt + completion tokens for the compression LLM call.</summary>
    public int? TotalTokens { get; private set; }

    /// <summary>True when <see cref="TotalTokens"/> came from local estimation rather than provider usage.</summary>
    public bool TokensAreEstimated { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    private CompressionEvent()
    {
    }

    public static CompressionEvent Start(
        Guid conversationId,
        CompressionMode mode,
        int originalTokens,
        int? workingMemoryVersionBefore,
        int foldedMessageCount,
        DateTimeOffset now)
    {
        return new CompressionEvent
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Mode = mode,
            Status = CompressionStatus.Running,
            OriginalTokens = originalTokens,
            WorkingMemoryVersionBefore = workingMemoryVersionBefore,
            FoldedMessageCount = foldedMessageCount,
            CreatedAt = now
        };
    }

    public double? CompressionRatio =>
        CompressedTokens.HasValue && OriginalTokens > 0
            ? Math.Round((double)CompressedTokens.Value / OriginalTokens, 4)
            : null;

    public void Succeed(
        int compressedTokens,
        int workingMemoryVersionAfter,
        DateTimeOffset completedAt,
        int? promptTokens = null,
        int? completionTokens = null,
        bool tokensAreEstimated = false)
    {
        Status = CompressionStatus.Succeeded;
        CompressedTokens = compressedTokens;
        WorkingMemoryVersionAfter = workingMemoryVersionAfter;
        CompletedAt = completedAt;
        DurationMs = (long)(completedAt - CreatedAt).TotalMilliseconds;
        PromptTokens = promptTokens;
        CompletionTokens = completionTokens;
        if (promptTokens.HasValue || completionTokens.HasValue)
        {
            TotalTokens = (promptTokens ?? 0) + (completionTokens ?? 0);
            TokensAreEstimated = tokensAreEstimated;
        }
    }

    public void Fail(string errorMessage, DateTimeOffset completedAt)
    {
        Status = CompressionStatus.Failed;
        ErrorMessage = errorMessage;
        CompletedAt = completedAt;
        DurationMs = (long)(completedAt - CreatedAt).TotalMilliseconds;
    }
}
