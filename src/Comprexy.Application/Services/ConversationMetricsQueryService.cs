using Comprexy.Application.Abstractions;
using Comprexy.Domain.Entities;

namespace Comprexy.Application.Services;

public sealed class ConversationMetricsQueryService : IConversationMetricsQueryService
{
    private readonly IConversationMetricsSummaryRepository _summaryRepository;
    private readonly IConversationTurnMetricRepository _turnMetricRepository;

    public ConversationMetricsQueryService(
        IConversationMetricsSummaryRepository summaryRepository,
        IConversationTurnMetricRepository turnMetricRepository)
    {
        _summaryRepository = summaryRepository;
        _turnMetricRepository = turnMetricRepository;
    }

    public Task<IReadOnlyList<ConversationMetricsSummary>> ListConversationSummariesAsync(
        CancellationToken cancellationToken) =>
        _summaryRepository.ListAsync(cancellationToken);

    public Task<ConversationMetricsSummary?> GetConversationSummaryAsync(
        Guid conversationId,
        CancellationToken cancellationToken) =>
        _summaryRepository.FindByConversationIdAsync(conversationId, cancellationToken);

    public Task<IReadOnlyList<ConversationTurnMetric>> ListTurnMetricsAsync(
        Guid conversationId,
        CancellationToken cancellationToken) =>
        _turnMetricRepository.ListByConversationIdAsync(conversationId, cancellationToken);
}
