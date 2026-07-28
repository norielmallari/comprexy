using Comprexy.Application.Abstractions;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Comprexy.Infrastructure.Persistence.Repositories;

public class EfCompressionEventRepository(ComprexyDbContext dbContext) : ICompressionEventRepository
{
    public void Add(CompressionEvent compressionEvent) => dbContext.CompressionEvents.Add(compressionEvent);

    public Task<CompressionEvent?> GetLatestSucceededAsync(
        Guid conversationId,
        CompressionMode mode,
        CancellationToken cancellationToken) =>
        dbContext.CompressionEvents
            .AsNoTracking()
            .Where(e => e.ConversationId == conversationId
                        && e.Mode == mode
                        && e.Status == CompressionStatus.Succeeded
                        && e.CompletedAt != null)
            .OrderByDescending(e => e.CompletedAt)
            .FirstOrDefaultAsync(cancellationToken);
}
