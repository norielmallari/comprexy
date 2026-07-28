using Comprexy.Application.Abstractions;
using Comprexy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Comprexy.Infrastructure.Persistence.Repositories;

public class EfConversationMessageRepository(ComprexyDbContext dbContext) : IConversationMessageRepository
{
    public Task<List<ConversationMessage>> GetByConversationIdAsync(Guid conversationId, CancellationToken cancellationToken) =>
        dbContext.ConversationMessages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.Sequence)
            .ToListAsync(cancellationToken);

    public Task<List<ConversationMessage>> GetUnfoldedAsync(Guid conversationId, CancellationToken cancellationToken) =>
        dbContext.ConversationMessages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.FoldedIntoWorkingMemoryVersion == null)
            .OrderBy(m => m.Sequence)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ConversationMessage>> ListBySequenceRangeAsync(
        Guid conversationId,
        int sequenceStart,
        int sequenceEnd,
        int take,
        CancellationToken cancellationToken)
    {
        if (take < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(take));
        }

        return await dbContext.ConversationMessages
            .AsNoTracking()
            .Where(m =>
                m.ConversationId == conversationId &&
                m.Sequence >= sequenceStart &&
                m.Sequence <= sequenceEnd)
            .OrderBy(m => m.Sequence)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ConversationMessage>> ListRecentAsync(
        Guid conversationId,
        int take,
        bool unfoldedOnly,
        CancellationToken cancellationToken)
    {
        if (take < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(take));
        }

        var query = dbContext.ConversationMessages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId);

        if (unfoldedOnly)
        {
            query = query.Where(m => m.FoldedIntoWorkingMemoryVersion == null);
        }

        var newestFirst = await query
            .OrderByDescending(m => m.Sequence)
            .Take(take)
            .ToListAsync(cancellationToken);

        newestFirst.Reverse();
        return newestFirst;
    }

    public async Task<IReadOnlyList<ConversationMessage>> SearchContentAsync(
        Guid conversationId,
        string query,
        bool includeFolded,
        int take,
        CancellationToken cancellationToken)
    {
        if (take < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(take));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var efQuery = dbContext.ConversationMessages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.Content.Contains(query));

        if (!includeFolded)
        {
            efQuery = efQuery.Where(m => m.FoldedIntoWorkingMemoryVersion == null);
        }

        return await efQuery
            .OrderByDescending(m => m.Sequence)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public void Add(ConversationMessage message) => dbContext.ConversationMessages.Add(message);

    public void Remove(ConversationMessage message) => dbContext.ConversationMessages.Remove(message);
}
