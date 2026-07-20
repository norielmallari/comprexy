using Comprexy.Domain.Enums;

namespace Comprexy.Domain.Entities;

/// <summary>
/// Diagnostic record of a single compression attempt, used for observability and troubleshooting.
/// </summary>
public class CompressionEvent
{
    public Guid Id { get; private set; }

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

    public void Succeed(int compressedTokens, int workingMemoryVersionAfter, DateTimeOffset completedAt)
    {
        Status = CompressionStatus.Succeeded;
        CompressedTokens = compressedTokens;
        WorkingMemoryVersionAfter = workingMemoryVersionAfter;
        CompletedAt = completedAt;
        DurationMs = (long)(completedAt - CreatedAt).TotalMilliseconds;
    }

    public void Fail(string errorMessage, DateTimeOffset completedAt)
    {
        Status = CompressionStatus.Failed;
        ErrorMessage = errorMessage;
        CompletedAt = completedAt;
        DurationMs = (long)(completedAt - CreatedAt).TotalMilliseconds;
    }
}
