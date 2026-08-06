using Comprexy.Domain.Entities;

namespace Comprexy.Domain.Entities;

/// <summary>
/// A single long-running logical conversation tracked by Comprexy, identified either by a
/// client-supplied key or by a fingerprint derived from the earliest messages of the exchange.
/// </summary>
public class Conversation : EntityBase
{
    /// <summary>Stable identity for this conversation (client header value or content fingerprint).</summary>
    public string ConversationKey { get; private set; } = string.Empty;

    /// <summary>
    /// Base system prompt (persona / env / non-rule preamble). Rule bodies are stripped at detect time
    /// and injected ephemerally on the live prepare path; this column stores BaseSystem only.
    /// </summary>
    public string? SystemPrompt { get; private set; }

    /// <summary>
    /// Number of messages (from the client's message array) already persisted as
    /// <see cref="ConversationMessage"/> rows. Used to diff incoming requests for new turns.
    /// </summary>
    public int SyncedMessageCount { get; private set; }

    /// <summary>
    /// Versioned allowlisted effective-settings JSON bound once on conversation create.
    /// Null for legacy rows (resolve uses live options; UI shows N/A). Never backfilled.
    /// </summary>
    public string? EffectiveSettingsJson { get; private set; }

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

    /// <summary>
    /// Sets BaseSystem on first capture or when ordinal-different from stored. Returns whether the
    /// column changed.
    /// </summary>
    public bool SetBaseSystem(string? baseSystem)
    {
        if (string.IsNullOrWhiteSpace(baseSystem))
        {
            return false;
        }

        if (SystemPrompt is not null && string.Equals(SystemPrompt, baseSystem, StringComparison.Ordinal))
        {
            return false;
        }

        SystemPrompt = baseSystem;
        return true;
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

    /// <summary>
    /// Binds sticky effective settings on first create only. No-op when already set (including
    /// empty string is treated as set — only null means unbound).
    /// </summary>
    public void BindEffectiveSettings(string effectiveSettingsJson, DateTimeOffset now)
    {
        if (EffectiveSettingsJson is not null)
        {
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(effectiveSettingsJson);
        EffectiveSettingsJson = effectiveSettingsJson;
        UpdatedAt = now;
    }
}
