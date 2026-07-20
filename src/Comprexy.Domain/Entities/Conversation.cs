namespace Comprexy.Domain.Entities;

/// <summary>
/// A single long-running logical conversation tracked by Comprexy, identified either by a
/// client-supplied key or by a fingerprint derived from the earliest messages of the exchange.
/// </summary>
public class Conversation
{
    public Guid Id { get; private set; }

    /// <summary>Stable identity for this conversation (client header value or content fingerprint).</summary>
    public string ConversationKey { get; private set; } = string.Empty;

    /// <summary>The system prompt captured on the first turn, reused on the outgoing context build.</summary>
    public string? SystemPrompt { get; private set; }

    /// <summary>
    /// Number of messages (from the client's message array) already persisted as
    /// <see cref="ConversationMessage"/> rows. Used to diff incoming requests for new turns.
    /// </summary>
    public int SyncedMessageCount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private Conversation()
    {
    }

    public static Conversation Create(string conversationKey, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(conversationKey))
        {
            throw new ArgumentException("Conversation key must not be empty.", nameof(conversationKey));
        }

        return new Conversation
        {
            Id = Guid.NewGuid(),
            ConversationKey = conversationKey,
            SyncedMessageCount = 0,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void CaptureSystemPromptIfAbsent(string? systemPrompt)
    {
        if (SystemPrompt is null && !string.IsNullOrWhiteSpace(systemPrompt))
        {
            SystemPrompt = systemPrompt;
        }
    }

    public void AdvanceSyncedMessageCount(int newlyPersistedCount, DateTimeOffset now)
    {
        if (newlyPersistedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(newlyPersistedCount));
        }

        SyncedMessageCount += newlyPersistedCount;
        UpdatedAt = now;
    }

    /// <summary>
    /// Sets the client-history sync cursor absolutely. Used to realign when the client rewinds
    /// or when we finish a turn and expect the next request to include our assistant message.
    /// </summary>
    public void SetSyncedMessageCount(int syncedMessageCount, DateTimeOffset now)
    {
        if (syncedMessageCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(syncedMessageCount));
        }

        SyncedMessageCount = syncedMessageCount;
        UpdatedAt = now;
    }

    public void Touch(DateTimeOffset now) => UpdatedAt = now;
}
