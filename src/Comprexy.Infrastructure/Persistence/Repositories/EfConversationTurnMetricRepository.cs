using Comprexy.Application.Abstractions;
using Comprexy.Application.Models.Telemetry;
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

    public async Task<IReadOnlyList<ConversationTurnProjection>> ListBoundedProjectionsAsync(
        Guid conversationId,
        int take,
        CancellationToken cancellationToken)
    {
        if (take < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(take));
        }

        return await dbContext.ConversationTurnMetrics
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.TurnIndex)
            .Take(take)
            .Select(m => new ConversationTurnProjection
            {
                TurnIndex = m.TurnIndex,
                RequestStartedAt = m.RequestStartedAt,
                Model = m.Model,
                RawInputTokensEstimated = m.RawInputTokensEstimated,
                IrFullInputTokensEstimated = m.IrFullInputTokensEstimated,
                CompressedInputTokensEstimated = m.CompressedInputTokensEstimated,
                ActualPromptTokens = m.ActualPromptTokens,
                ActualCompletionTokens = m.ActualCompletionTokens,
                BaselineTotalTokensEstimated = m.BaselineTotalTokensEstimated,
                CompressedTotalTokensEstimated = m.CompressedTotalTokensEstimated,
                NetTokensSaved = m.NetTokensSaved,
                NetTokenSavingsRatio = m.NetTokenSavingsRatio,
                VirtualToolsTokensSaved = m.VirtualToolsTokensSaved,
                SoftBudgetExceeded = m.SoftBudgetExceeded,
                HardBudgetExceeded = m.HardBudgetExceeded,
                TrimTriggered = m.TrimTriggered,
                WorkingMemoryVersionUsed = m.WorkingMemoryVersionUsed,
                RawMessageCount = m.RawMessageCount,
                SentMessageCount = m.SentMessageCount,
                DurationMs = m.DurationMs,
                UpstreamDurationMs = m.UpstreamDurationMs,
                PrepareDurationMs = m.PrepareDurationMs,
                CreatedAt = m.CreatedAt
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<ConversationTurnProjection?> GetFinalTurnProjectionAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        return await dbContext.ConversationTurnMetrics
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.TurnIndex)
            .Select(m => new ConversationTurnProjection
            {
                TurnIndex = m.TurnIndex,
                RequestStartedAt = m.RequestStartedAt,
                Model = m.Model,
                RawInputTokensEstimated = m.RawInputTokensEstimated,
                IrFullInputTokensEstimated = m.IrFullInputTokensEstimated,
                CompressedInputTokensEstimated = m.CompressedInputTokensEstimated,
                ActualPromptTokens = m.ActualPromptTokens,
                ActualCompletionTokens = m.ActualCompletionTokens,
                BaselineTotalTokensEstimated = m.BaselineTotalTokensEstimated,
                CompressedTotalTokensEstimated = m.CompressedTotalTokensEstimated,
                NetTokensSaved = m.NetTokensSaved,
                NetTokenSavingsRatio = m.NetTokenSavingsRatio,
                VirtualToolsTokensSaved = m.VirtualToolsTokensSaved,
                SoftBudgetExceeded = m.SoftBudgetExceeded,
                HardBudgetExceeded = m.HardBudgetExceeded,
                TrimTriggered = m.TrimTriggered,
                WorkingMemoryVersionUsed = m.WorkingMemoryVersionUsed,
                RawMessageCount = m.RawMessageCount,
                SentMessageCount = m.SentMessageCount,
                DurationMs = m.DurationMs,
                UpstreamDurationMs = m.UpstreamDurationMs,
                PrepareDurationMs = m.PrepareDurationMs,
                CreatedAt = m.CreatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ConversationTurnSavingsAggregates?> GetSavingsAggregatesAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        return await dbContext.ConversationTurnMetrics
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .GroupBy(_ => 1)
            .Select(g => new ConversationTurnSavingsAggregates
            {
                PeakNetTokenSavingsRatio = g.Max(m => m.NetTokenSavingsRatio),
                SimpleAverageNetTokenSavingsRatio = g.Average(m => m.NetTokenSavingsRatio),
                TurnCount = g.Count()
            })
            .FirstOrDefaultAsync(cancellationToken);
    }
}
