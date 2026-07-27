using Comprexy.Application.Models;
using Comprexy.Application.Models.Telemetry;
using Comprexy.Domain.Entities;

namespace Comprexy.Application.Abstractions;

public interface IConversationTurnMetricRepository
{
    void Add(ConversationTurnMetric metric);

    Task<int> GetMaxTurnIndexAsync(Guid conversationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationTurnMetric>> ListByConversationIdAsync(
        Guid conversationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Bounded <see cref="ConversationTurnMetric"/> projection ordered by <c>TurnIndex</c>.
    /// Callers must clamp <paramref name="take"/> with <see cref="TelemetryQueryLimits"/> before calling.
    /// </summary>
    Task<IReadOnlyList<ConversationTurnProjection>> ListBoundedProjectionsAsync(
        Guid conversationId,
        int take,
        CancellationToken cancellationToken);

    Task<ConversationTurnProjection?> GetFinalTurnProjectionAsync(
        Guid conversationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Whole-conversation peak and simple-average savings ratios (EF aggregates; not row-capped).
    /// </summary>
    Task<ConversationTurnSavingsAggregates?> GetSavingsAggregatesAsync(
        Guid conversationId,
        CancellationToken cancellationToken);
}

public interface IConversationMetricsSummaryRepository
{
    Task<ConversationMetricsSummary?> FindByConversationIdAsync(
        Guid conversationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// No-tracking rollup projection for telemetry and operator reads.
    /// </summary>
    Task<ConversationSummaryRollup?> GetRollupAsync(
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

    /// <summary>
    /// Returns true when a conversation row exists (metrics may still be empty).
    /// </summary>
    Task<bool> ConversationExistsAsync(Guid conversationId, CancellationToken cancellationToken);

    Task<ConversationSummaryDto?> GetTelemetrySummaryAsync(
        Guid conversationId,
        int? maxTurns,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationTurnDto>> GetTelemetryTurnsAsync(
        Guid conversationId,
        int? maxTurns,
        CancellationToken cancellationToken);

    Task<FinalTurnSnapshotDto?> GetFinalTurnSnapshotAsync(
        Guid conversationId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationPhaseDto>> GetPhaseBreakdownAsync(
        Guid conversationId,
        int? maxTurns,
        CancellationToken cancellationToken);

    Task<ConversationBudgetEventDto?> GetBudgetEventsAsync(
        Guid conversationId,
        int? maxTurns,
        CancellationToken cancellationToken);

    Task<PromptGrowthTimelineDto?> GetPromptGrowthTimelineAsync(
        Guid conversationId,
        int? maxTurns,
        CancellationToken cancellationToken);

    Task<string?> GetEvidenceMarkdownAsync(
        Guid conversationId,
        int? maxTurns,
        CancellationToken cancellationToken);

    Task<ConversationComparisonDto?> CompareConversationsAsync(
        Guid leftConversationId,
        Guid rightConversationId,
        int? maxTurns,
        CancellationToken cancellationToken);
}
