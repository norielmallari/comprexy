using Comprexy.Domain.Entities;

namespace Comprexy.Application.Abstractions;

public interface IConversationRepository
{
    Task<Conversation?> FindByKeyAsync(string conversationKey, CancellationToken cancellationToken);

    Task<Conversation?> FindByIdAsync(Guid conversationId, CancellationToken cancellationToken);

    /// <summary>
    /// No-tracking existence check for read-only callers (telemetry MCP, etc.).
    /// </summary>
    Task<bool> ExistsAsync(Guid conversationId, CancellationToken cancellationToken);

    void Add(Conversation conversation);
}
