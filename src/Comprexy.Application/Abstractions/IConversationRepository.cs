using Comprexy.Domain.Entities;

namespace Comprexy.Application.Abstractions;

public interface IConversationRepository
{
    Task<Conversation?> FindByKeyAsync(string conversationKey, CancellationToken cancellationToken);

    Task<Conversation?> FindByIdAsync(Guid conversationId, CancellationToken cancellationToken);

    void Add(Conversation conversation);
}
