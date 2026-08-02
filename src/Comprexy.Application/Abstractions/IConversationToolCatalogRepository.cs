using Comprexy.Domain.Entities;

namespace Comprexy.Application.Abstractions;

public interface IConversationToolCatalogRepository
{
    Task<ConversationToolCatalog?> GetByConversationIdAsync(Guid conversationId, CancellationToken cancellationToken);

    /// <summary>
    /// Load-modify-save entry point: returns a change-tracked catalog row (Local first, else tracking query).
    /// </summary>
    Task<ConversationToolCatalog?> GetTrackedByConversationIdAsync(Guid conversationId, CancellationToken cancellationToken);

    void Add(ConversationToolCatalog catalog);
}
