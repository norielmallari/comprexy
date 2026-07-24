using Comprexy.Application.Abstractions;
using Comprexy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Comprexy.Infrastructure.Persistence.Repositories;

public sealed class EfConversationTurnMetricRepository(ComprexyDbContext dbContext)
    : IConversationTurnMetricRepository
{
    public void Add(ConversationTurnMetric metric) =>
        dbContext.ConversationTurnMetrics.Add(metric);

    public async Task<int> GetMaxTurnIndexAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var max = await dbContext.ConversationTurnMetrics
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .Select(m => (int?)m.TurnIndex)
            .MaxAsync(cancellationToken);

        return max ?? 0;
    }

    public async Task<IReadOnlyList<ConversationTurnMetric>> ListByConversationIdAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        return await dbContext.ConversationTurnMetrics
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.TurnIndex)
            .ToListAsync(cancellationToken);
    }
}
