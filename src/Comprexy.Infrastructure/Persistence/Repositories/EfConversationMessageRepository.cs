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

    public void Add(ConversationMessage message) => dbContext.ConversationMessages.Add(message);
}
