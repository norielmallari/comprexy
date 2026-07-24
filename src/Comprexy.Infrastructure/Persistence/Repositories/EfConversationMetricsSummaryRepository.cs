using Comprexy.Application.Abstractions;
using Comprexy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Comprexy.Infrastructure.Persistence.Repositories;

public sealed class EfConversationMetricsSummaryRepository(ComprexyDbContext dbContext)
    : IConversationMetricsSummaryRepository
{
    public void Add(ConversationMetricsSummary summary) =>
        dbContext.ConversationMetricsSummaries.Add(summary);

    public Task<ConversationMetricsSummary?> FindByConversationIdAsync(
        Guid conversationId,
        CancellationToken cancellationToken) =>
        dbContext.ConversationMetricsSummaries
            .FirstOrDefaultAsync(s => s.ConversationId == conversationId, cancellationToken);

    public async Task<IReadOnlyList<ConversationMetricsSummary>> ListAsync(CancellationToken cancellationToken)
    {
        return await dbContext.ConversationMetricsSummaries
            .AsNoTracking()
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(cancellationToken);
    }
}
