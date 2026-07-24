using Comprexy.Application.Abstractions;
using Comprexy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Comprexy.Infrastructure.Persistence.Repositories;

public class EfConversationToolCatalogRepository(ComprexyDbContext dbContext) : IConversationToolCatalogRepository
{
    public async Task<ConversationToolCatalog?> GetByConversationIdAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var tracked = dbContext.ConversationToolCatalogs.Local
            .FirstOrDefault(c => c.ConversationId == conversationId);
        if (tracked is not null)
        {
            return tracked;
        }

        return await dbContext.ConversationToolCatalogs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ConversationId == conversationId, cancellationToken);
    }

    public void Add(ConversationToolCatalog catalog) => dbContext.ConversationToolCatalogs.Add(catalog);
}
