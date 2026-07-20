using Comprexy.Domain.Enums;

namespace Comprexy.Domain.Entities;

/// <summary>
/// A single raw message (user, assistant, or tool) belonging to a conversation. Messages remain
/// until folded into a <see cref="WorkingMemory"/> version by a compression job.
/// </summary>
public class ConversationMessage
{
    public Guid Id { get; private set; }

    public Guid ConversationId { get; private set; }

    /// <summary>Ordinal position of this message within the conversation, starting at 0.</summary>
    public int Sequence { get; private set; }

    public MessageRole Role { get; private set; }

    public string Content { get; private set; } = string.Empty;

    /// <summary>
    /// Original OpenAI message JSON when available (tool_calls, multimodal parts, etc.).
    /// Used when rebuilding the sliding recent window for upstream requests.
    /// </summary>
    public string? RawWireJson { get; private set; }

    public int TokenCount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Set once this message has been summarized into a working memory version and is no longer
    /// sent to the model as raw context. Null means the message is still "recent" raw context.
    /// </summary>
    public int? FoldedIntoWorkingMemoryVersion { get; private set; }

    private ConversationMessage()
    {
    }

    public static ConversationMessage Create(
        Guid conversationId,
        int sequence,
        MessageRole role,
        string content,
        int tokenCount,
        DateTimeOffset now,
        string? rawWireJson = null)
    {
        return new ConversationMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Sequence = sequence,
            Role = role,
            Content = content,
            TokenCount = tokenCount,
            CreatedAt = now,
            RawWireJson = rawWireJson
        };
    }

    public bool IsFolded => FoldedIntoWorkingMemoryVersion.HasValue;

    public bool HasWireJson => !string.IsNullOrWhiteSpace(RawWireJson);

    public void MarkFoldedInto(int workingMemoryVersion)
    {
        FoldedIntoWorkingMemoryVersion = workingMemoryVersion;
    }

    /// <summary>
    /// Fills missing wire JSON / content when a later client replay has a richer message
    /// (typical for assistant tool-call turns we originally persisted as empty text).
    /// </summary>
    public void EnrichFromClient(string content, string? rawWireJson, int tokenCount)
    {
        if (!string.IsNullOrWhiteSpace(rawWireJson) && !HasWireJson)
        {
            RawWireJson = rawWireJson;
        }

        if (string.IsNullOrWhiteSpace(Content) && !string.IsNullOrWhiteSpace(content))
        {
            Content = content;
        }

        if (tokenCount > TokenCount)
        {
            TokenCount = tokenCount;
        }
    }
}
