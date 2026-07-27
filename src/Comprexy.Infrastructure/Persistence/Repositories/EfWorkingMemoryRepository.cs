using Comprexy.Application.Abstractions;
using Comprexy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Comprexy.Infrastructure.Persistence.Repositories;

public class EfWorkingMemoryRepository(ComprexyDbContext dbContext) : IWorkingMemoryRepository
{
    public Task<WorkingMemory?> GetLatestAsync(Guid conversationId, CancellationToken cancellationToken) =>
        dbContext.WorkingMemories
            .AsNoTracking()
            .Where(w => w.ConversationId == conversationId)
            .OrderByDescending(w => w.Version)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<WorkingMemory?> GetByVersionAsync(
        Guid conversationId,
        int version,
        CancellationToken cancellationToken) =>
        dbContext.WorkingMemories
            .AsNoTracking()
            .Where(w => w.ConversationId == conversationId && w.Version == version)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkingMemory>> SearchContentAsync(
        Guid conversationId,
        string query,
        int take,
        CancellationToken cancellationToken)
    {
        if (take < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(take));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        return await dbContext.WorkingMemories
            .AsNoTracking()
            .Where(w => w.ConversationId == conversationId && w.Content.Contains(query))
            .OrderByDescending(w => w.Version)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public void Add(WorkingMemory workingMemory) => dbContext.WorkingMemories.Add(workingMemory);
}
