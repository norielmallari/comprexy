using Comprexy.Application.Abstractions;
using Comprexy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Comprexy.Infrastructure.Persistence.Repositories;

public class EfConversationRepository(ComprexyDbContext dbContext) : IConversationRepository
{
    public Task<Conversation?> FindByKeyAsync(string conversationKey, CancellationToken cancellationToken) =>
        dbContext.Conversations.FirstOrDefaultAsync(c => c.ConversationKey == conversationKey, cancellationToken);

    public Task<Conversation?> FindByIdAsync(Guid conversationId, CancellationToken cancellationToken) =>
        dbContext.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

    public void Add(Conversation conversation) => dbContext.Conversations.Add(conversation);
}
