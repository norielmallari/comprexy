using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Domain.Entities;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services;

public sealed class ConversationMetricsRecorder : IConversationMetricsRecorder
{
    private readonly IConversationTurnMetricRepository _turnMetricRepository;
    private readonly IConversationMetricsSummaryRepository _summaryRepository;
    private readonly IClock _clock;
    private readonly MetricsOptions _options;

    public ConversationMetricsRecorder(
        IConversationTurnMetricRepository turnMetricRepository,
        IConversationMetricsSummaryRepository summaryRepository,
        IClock clock,
        IOptions<MetricsOptions> options)
    {
        _turnMetricRepository = turnMetricRepository;
        _summaryRepository = summaryRepository;
        _clock = clock;
        _options = options.Value;
    }

    public bool IsEnabled => _options.Enabled;

    public async Task RecordSuccessfulTurnAsync(
        SuccessfulTurnMetricInput input,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var now = _clock.UtcNow;
        var turnIndex = await _turnMetricRepository.GetMaxTurnIndexAsync(input.ConversationId, cancellationToken) + 1;
        var completionTokens = input.ActualCompletionTokens ?? input.EstimatedCompletionTokensFallback;

        var softExceeded = input.BudgetDecision is ContextBudgetDecision.ForwardWithHighPriorityCompression
            or ContextBudgetDecision.EmergencyCompressionRequired;
        var hardExceeded = input.BudgetDecision == ContextBudgetDecision.EmergencyCompressionRequired;

        var turn = ConversationTurnMetric.Create(
            input.ConversationId,
            turnIndex,
            input.RequestStartedAt,
            input.Model,
            input.RawInputTokensEstimated,
            input.CompressedInputTokensEstimated,
            input.ActualPromptTokens,
            completionTokens,
            softExceeded,
            hardExceeded,
            input.TrimTriggered,
            input.WorkingMemoryVersionUsed,
            input.RawMessageCount,
            input.SentMessageCount,
            input.RequestHash,
            input.SentPayloadHash,
            now);

        _turnMetricRepository.Add(turn);

        var summary = await _summaryRepository.FindByConversationIdAsync(input.ConversationId, cancellationToken);
        if (summary is null)
        {
            summary = ConversationMetricsSummary.Create(input.ConversationId, now);
            _summaryRepository.Add(summary);
        }

        summary.ApplyTurn(turn, now);
    }

    public async Task RecordCompressionOverheadAsync(
        Guid conversationId,
        int overheadTokens,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled || overheadTokens <= 0)
        {
            return;
        }

        var now = _clock.UtcNow;
        var summary = await _summaryRepository.FindByConversationIdAsync(conversationId, cancellationToken);
        if (summary is null)
        {
            summary = ConversationMetricsSummary.Create(conversationId, now);
            _summaryRepository.Add(summary);
        }

        summary.ApplyCompressionOverhead(overheadTokens, now);
    }
}
