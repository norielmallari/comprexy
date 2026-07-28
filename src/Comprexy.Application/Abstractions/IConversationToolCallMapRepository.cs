using Comprexy.Domain.Entities;

namespace Comprexy.Application.Abstractions;

/// <summary>
/// Durable store for open IR↔client tool_call_id mappings (Virtual Tools dual identity).
/// </summary>
public interface IConversationToolCallMapRepository
{
    void Add(ConversationToolCallMap map);

    Task<ConversationToolCallMap?> FindPendingByClientCallIdAsync(
        Guid conversationId,
        string clientCallId,
        CancellationToken cancellationToken);

    Task<ConversationToolCallMap?> FindPendingByIrCallIdAsync(
        Guid conversationId,
        string irCallId,
        CancellationToken cancellationToken);

    /// <summary>Removes the pending row for the client call id when present.</summary>
    Task DeleteByClientCallIdAsync(Guid conversationId, string clientCallId, CancellationToken cancellationToken);

    /// <summary>Removes all pending rows for the conversation.</summary>
    Task DeletePendingByConversationIdAsync(Guid conversationId, CancellationToken cancellationToken);

    /// <summary>Removes pending rows whose <see cref="ConversationToolCallMap.RegisteredAt"/> is at or before the cutoff. Returns deleted count.</summary>
    Task<int> DeleteExpiredPendingAsync(DateTimeOffset olderThanUtc, CancellationToken cancellationToken);
}
