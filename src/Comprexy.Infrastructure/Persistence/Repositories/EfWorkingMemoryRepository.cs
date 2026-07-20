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

    public void Add(WorkingMemory workingMemory) => dbContext.WorkingMemories.Add(workingMemory);
}
