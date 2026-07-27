using Comprexy.Application.Abstractions;
using Comprexy.Application.Models.Telemetry;
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

    public Task<ConversationSummaryRollup?> GetRollupAsync(
        Guid conversationId,
        CancellationToken cancellationToken) =>
        dbContext.ConversationMetricsSummaries
            .AsNoTracking()
            .Where(s => s.ConversationId == conversationId)
            .Select(s => new ConversationSummaryRollup
            {
                ConversationId = s.ConversationId,
                TotalTurns = s.TotalTurns,
                TotalRawInputTokensEstimated = s.TotalRawInputTokensEstimated,
                TotalCompressedPromptTokens = s.TotalCompressedPromptTokens,
                TotalCompletionTokens = s.TotalCompletionTokens,
                TotalCompressionOverheadTokens = s.TotalCompressionOverheadTokens,
                TotalBaselineTokensEstimated = s.TotalBaselineTokensEstimated,
                TotalActualTokensEstimated = s.TotalActualTokensEstimated,
                TotalNetTokensSaved = s.TotalNetTokensSaved,
                AverageTokenSavingsRatio = s.AverageTokenSavingsRatio,
                CompressionEventCount = s.CompressionEventCount,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ConversationMetricsSummary>> ListAsync(CancellationToken cancellationToken)
    {
        return await dbContext.ConversationMetricsSummaries
            .AsNoTracking()
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(cancellationToken);
    }
}
