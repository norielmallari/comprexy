using Comprexy.Application.Abstractions;
using Comprexy.Domain.Entities;

namespace Comprexy.Application.Tests.Services;

/// <summary>Process-local stand-in for EF dual-id map rows in unit tests.</summary>
internal sealed class InMemoryConversationToolCallMapRepository : IConversationToolCallMapRepository
{
    private readonly List<ConversationToolCallMap> _rows = [];

    public IReadOnlyList<ConversationToolCallMap> Rows => _rows;

    public void Add(ConversationToolCallMap map) => _rows.Add(map);

    public Task<ConversationToolCallMap?> FindPendingByClientCallIdAsync(
        Guid conversationId,
        string clientCallId,
        CancellationToken cancellationToken)
    {
        var row = _rows.FirstOrDefault(m =>
            m.ConversationId == conversationId &&
            m.Pending &&
            string.Equals(m.ClientCallId, clientCallId, StringComparison.Ordinal));
        return Task.FromResult(row);
    }

    public Task<ConversationToolCallMap?> FindPendingByIrCallIdAsync(
        Guid conversationId,
        string irCallId,
        CancellationToken cancellationToken)
    {
        var row = _rows.FirstOrDefault(m =>
            m.ConversationId == conversationId &&
            m.Pending &&
            string.Equals(m.IrCallId, irCallId, StringComparison.Ordinal));
        return Task.FromResult(row);
    }

    public Task DeleteByClientCallIdAsync(
        Guid conversationId,
        string clientCallId,
        CancellationToken cancellationToken)
    {
        _rows.RemoveAll(m =>
            m.ConversationId == conversationId &&
            string.Equals(m.ClientCallId, clientCallId, StringComparison.Ordinal));
        return Task.CompletedTask;
    }

    public Task DeletePendingByConversationIdAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        _rows.RemoveAll(m => m.ConversationId == conversationId && m.Pending);
        return Task.CompletedTask;
    }

    public Task<int> DeleteExpiredPendingAsync(DateTimeOffset olderThanUtc, CancellationToken cancellationToken)
    {
        var expired = _rows
            .Where(m => m.Pending && m.RegisteredAt <= olderThanUtc)
            .ToList();
        foreach (var row in expired)
        {
            _rows.Remove(row);
        }

        return Task.FromResult(expired.Count);
    }
}
