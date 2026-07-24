using Comprexy.Domain.Entities;

namespace Comprexy.Application.Abstractions;

public interface IConversationToolDefinitionRepository
{
    Task<IReadOnlyList<ConversationToolDefinition>> GetByConversationIdAsync(
        Guid conversationId,
        CancellationToken cancellationToken);

    Task<ConversationToolDefinition?> FindAsync(
        Guid conversationId,
        string toolName,
        CancellationToken cancellationToken);

    void Add(ConversationToolDefinition definition);
}
