using Comprexy.Application.Abstractions;
using Comprexy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Comprexy.Infrastructure.Persistence.Repositories;

public class EfConversationToolCallMapRepository(ComprexyDbContext dbContext) : IConversationToolCallMapRepository
{
    public void Add(ConversationToolCallMap map) => dbContext.ConversationToolCallMaps.Add(map);

    public async Task<ConversationToolCallMap?> FindPendingByClientCallIdAsync(
        Guid conversationId,
        string clientCallId,
        CancellationToken cancellationToken)
    {
        var tracked = dbContext.ConversationToolCallMaps.Local
            .FirstOrDefault(m =>
                m.ConversationId == conversationId &&
                m.Pending &&
                string.Equals(m.ClientCallId, clientCallId, StringComparison.Ordinal));
        if (tracked is not null)
        {
            return tracked;
        }

        return await dbContext.ConversationToolCallMaps
            .FirstOrDefaultAsync(
                m => m.ConversationId == conversationId &&
                     m.Pending &&
                     m.ClientCallId == clientCallId,
                cancellationToken);
    }

    public async Task<ConversationToolCallMap?> FindPendingByIrCallIdAsync(
        Guid conversationId,
        string irCallId,
        CancellationToken cancellationToken)
    {
        var tracked = dbContext.ConversationToolCallMaps.Local
            .FirstOrDefault(m =>
                m.ConversationId == conversationId &&
                m.Pending &&
                string.Equals(m.IrCallId, irCallId, StringComparison.Ordinal));
        if (tracked is not null)
        {
            return tracked;
        }

        return await dbContext.ConversationToolCallMaps
            .FirstOrDefaultAsync(
                m => m.ConversationId == conversationId &&
                     m.Pending &&
                     m.IrCallId == irCallId,
                cancellationToken);
    }

    public async Task DeleteByClientCallIdAsync(
        Guid conversationId,
        string clientCallId,
        CancellationToken cancellationToken)
    {
        var row = await FindPendingByClientCallIdAsync(conversationId, clientCallId, cancellationToken);
        if (row is not null)
        {
            dbContext.ConversationToolCallMaps.Remove(row);
        }
    }

    public async Task DeletePendingByConversationIdAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.ConversationToolCallMaps
            .Where(m => m.ConversationId == conversationId && m.Pending)
            .ToListAsync(cancellationToken);
        if (rows.Count > 0)
        {
            dbContext.ConversationToolCallMaps.RemoveRange(rows);
        }
    }

    public async Task<int> DeleteExpiredPendingAsync(
        DateTimeOffset olderThanUtc,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.ConversationToolCallMaps
            .Where(m => m.Pending && m.RegisteredAt <= olderThanUtc)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return 0;
        }

        dbContext.ConversationToolCallMaps.RemoveRange(rows);
        return rows.Count;
    }
}
