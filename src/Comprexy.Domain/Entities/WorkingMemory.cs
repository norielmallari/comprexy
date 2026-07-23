namespace Comprexy.Domain.Entities;

/// <summary>
/// An immutable, versioned snapshot of the compact rolling working memory for a conversation.
/// New compressions append a new version rather than mutating an existing one, so a failed
/// compression can never corrupt the last known-good working memory.
/// </summary>
public class WorkingMemory : EntityBase
{
    public Guid ConversationId { get; private set; }

    /// <summary>Monotonically increasing version number, starting at 1.</summary>
    public int Version { get; private set; }

    /// <summary>Structured markdown content (Current Goal, Active Task, Key Decisions, ...).</summary>
    public string Content { get; private set; } = string.Empty;

    public int TokenCount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private WorkingMemory()
    {
    }

    public static WorkingMemory Create(
        Guid conversationId,
        int version,
        string content,
        int tokenCount,
        DateTimeOffset now)
    {
        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Working memory version must start at 1.");
        }

        return new WorkingMemory
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Version = version,
            Content = content,
            TokenCount = tokenCount,
            CreatedAt = now
        };
    }
}
