using Comprexy.Domain.Entities;

namespace Comprexy.Application.Abstractions;

public interface IConversationToolCatalogRepository
{
    Task<ConversationToolCatalog?> GetByConversationIdAsync(Guid conversationId, CancellationToken cancellationToken);

    void Add(ConversationToolCatalog catalog);
}
