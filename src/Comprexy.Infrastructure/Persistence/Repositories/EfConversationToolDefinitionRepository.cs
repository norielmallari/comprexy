using Comprexy.Application.Abstractions;
using Comprexy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Comprexy.Infrastructure.Persistence.Repositories;

public class EfConversationToolDefinitionRepository(ComprexyDbContext dbContext)
    : IConversationToolDefinitionRepository
{
    public async Task<IReadOnlyList<ConversationToolDefinition>> GetByConversationIdAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        return await dbContext.ConversationToolDefinitions
            .AsNoTracking()
            .Where(d => d.ConversationId == conversationId)
            .OrderBy(d => d.ToolName)
            .ToListAsync(cancellationToken);
    }

    public Task<ConversationToolDefinition?> FindAsync(
        Guid conversationId,
        string toolName,
        CancellationToken cancellationToken) =>
        dbContext.ConversationToolDefinitions
            .FirstOrDefaultAsync(
                d => d.ConversationId == conversationId && d.ToolName == toolName,
                cancellationToken);

    public void Add(ConversationToolDefinition definition) =>
        dbContext.ConversationToolDefinitions.Add(definition);
}
