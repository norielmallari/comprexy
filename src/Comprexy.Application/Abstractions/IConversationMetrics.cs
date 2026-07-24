using Comprexy.Application.Models;
using Comprexy.Domain.Entities;

namespace Comprexy.Application.Abstractions;

public interface IConversationTurnMetricRepository
{
    void Add(ConversationTurnMetric metric);

    Task<int> GetMaxTurnIndexAsync(Guid conversationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationTurnMetric>> ListByConversationIdAsync(
        Guid conversationId,
        CancellationToken cancellationToken);
}

public interface IConversationMetricsSummaryRepository
{
    Task<ConversationMetricsSummary?> FindByConversationIdAsync(
        Guid conversationId,
        CancellationToken cancellationToken);

    void Add(ConversationMetricsSummary summary);

    Task<IReadOnlyList<ConversationMetricsSummary>> ListAsync(CancellationToken cancellationToken);
}

public interface IConversationMetricsRecorder
{
    bool IsEnabled { get; }

    Task RecordSuccessfulTurnAsync(SuccessfulTurnMetricInput input, CancellationToken cancellationToken);

    Task RecordCompressionOverheadAsync(
        Guid conversationId,
        int overheadTokens,
        CancellationToken cancellationToken);
}

public interface IConversationMetricsQueryService
{
    Task<IReadOnlyList<ConversationMetricsSummary>> ListConversationSummariesAsync(
        CancellationToken cancellationToken);

    Task<ConversationMetricsSummary?> GetConversationSummaryAsync(
        Guid conversationId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationTurnMetric>> ListTurnMetricsAsync(
        Guid conversationId,
        CancellationToken cancellationToken);
}
