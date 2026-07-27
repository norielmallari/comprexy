using Comprexy.Domain.Entities;

namespace Comprexy.Application.Abstractions;

public interface IConversationMessageRepository
{
    /// <summary>All messages for a conversation, ordered by <see cref="ConversationMessage.Sequence"/>.</summary>
    Task<List<ConversationMessage>> GetByConversationIdAsync(Guid conversationId, CancellationToken cancellationToken);

    /// <summary>Messages not yet folded into a working memory version, ordered by sequence.</summary>
    Task<List<ConversationMessage>> GetUnfoldedAsync(Guid conversationId, CancellationToken cancellationToken);

    /// <summary>
    /// Bounded sequence window (inclusive), ordered by <see cref="ConversationMessage.Sequence"/>.
    /// Callers must clamp <paramref name="take"/> before calling.
    /// </summary>
    Task<IReadOnlyList<ConversationMessage>> ListBySequenceRangeAsync(
        Guid conversationId,
        int sequenceStart,
        int sequenceEnd,
        int take,
        CancellationToken cancellationToken);

    /// <summary>
    /// Most recent messages by sequence (returned ascending). Callers must clamp <paramref name="take"/>.
    /// </summary>
    Task<IReadOnlyList<ConversationMessage>> ListRecentAsync(
        Guid conversationId,
        int take,
        bool unfoldedOnly,
        CancellationToken cancellationToken);

    /// <summary>
    /// Substring match on <see cref="ConversationMessage.Content"/>, newest sequences first.
    /// Callers must clamp <paramref name="take"/>.
    /// </summary>
    Task<IReadOnlyList<ConversationMessage>> SearchContentAsync(
        Guid conversationId,
        string query,
        bool includeFolded,
        int take,
        CancellationToken cancellationToken);

    void Add(ConversationMessage message);
}
