using Comprexy.Domain.Entities;

namespace Comprexy.Application.Abstractions;

public interface IConversationMessageRepository
{
    /// <summary>All messages for a conversation, ordered by <see cref="ConversationMessage.Sequence"/>.</summary>
    Task<List<ConversationMessage>> GetByConversationIdAsync(Guid conversationId, CancellationToken cancellationToken);

    /// <summary>Messages not yet folded into a working memory version, ordered by sequence.</summary>
    Task<List<ConversationMessage>> GetUnfoldedAsync(Guid conversationId, CancellationToken cancellationToken);

    void Add(ConversationMessage message);
}
