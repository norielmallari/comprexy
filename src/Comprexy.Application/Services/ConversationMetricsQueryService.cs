using Comprexy.Application.Abstractions;
using Comprexy.Application.Models.Telemetry;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Services;

public sealed class ConversationMetricsQueryService : IConversationMetricsQueryService
{
    private readonly IConversationMetricsSummaryRepository _summaryRepository;
    private readonly IConversationTurnMetricRepository _turnMetricRepository;
    private readonly IConversationRepository _conversationRepository;
    private readonly IWorkingMemoryRepository _workingMemoryRepository;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly IEvidenceMarkdownService _evidenceMarkdownService;
    private readonly IRegressionDetector _regressionDetector;
    private readonly PromptTokenBasisContext _promptTokenBasis;

    public ConversationMetricsQueryService(
        IConversationMetricsSummaryRepository summaryRepository,
        IConversationTurnMetricRepository turnMetricRepository,
        IConversationRepository conversationRepository,
        IWorkingMemoryRepository workingMemoryRepository,
        ITokenEstimator tokenEstimator,
        IEvidenceMarkdownService evidenceMarkdownService,
        IRegressionDetector regressionDetector,
        PromptTokenBasisContext promptTokenBasis)
    {
        _summaryRepository = summaryRepository;
        _turnMetricRepository = turnMetricRepository;
        _conversationRepository = conversationRepository;
        _workingMemoryRepository = workingMemoryRepository;
        _tokenEstimator = tokenEstimator;
        _evidenceMarkdownService = evidenceMarkdownService;
        _regressionDetector = regressionDetector;
        _promptTokenBasis = promptTokenBasis;
    }

    private PromptTokenBasis Basis => _promptTokenBasis.Resolve();

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

    public async Task<IReadOnlyList<ConversationTurnContextBreakdown>> ListTurnContextBreakdownsAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        // Same DbContext scope: sequential EF calls only.
        var turns = await _turnMetricRepository.ListByConversationIdAsync(conversationId, cancellationToken);
        if (turns.Count == 0)
        {
            return [];
        }

        var conversation = await _conversationRepository.FindByIdAsync(conversationId, cancellationToken);
        var workingMemoryVersions = await _workingMemoryRepository.ListVersionTokenCountsAsync(
            conversationId,
            cancellationToken);

        // Conversation.SystemPrompt stores BaseSystem (rules stripped). When BaseSystem was not
        // captured, System is 0. Catalog/rules segments are persisted at prepare; history residual
        // soaks array framing, tool_choice/response_format, and any ProviderActual gap.
        var systemPromptTokens = string.IsNullOrWhiteSpace(conversation?.SystemPrompt)
            ? 0
            : _tokenEstimator.CountTokens(conversation.SystemPrompt);

        var tokensByVersion = workingMemoryVersions.ToDictionary(w => w.Version, w => w.TokenCount);

        return turns
            .Select(turn =>
            {
                var workingMemoryTokens = turn.WorkingMemoryVersionUsed is int version
                    && tokensByVersion.TryGetValue(version, out var stored)
                        ? stored
                        : 0;

                var preparedPromptTokens = Basis == PromptTokenBasis.ProviderActual
                    && turn.ActualPromptTokens is int actual
                    && actual > 0
                        ? actual
                        : turn.CompressedInputTokensEstimated;

                var virtualCatalog = turn.PreparedVirtualToolSchemaTokensEstimated;
                var clientCatalog = turn.PreparedClientToolSchemaTokensEstimated;
                var rulesTokens = turn.PreparedRulesTokensEstimated;

                // Clamp only the residual: named segments stay at prepare/stored estimates.
                var historyTokens = Math.Max(
                    0,
                    preparedPromptTokens
                    - systemPromptTokens
                    - workingMemoryTokens
                    - virtualCatalog
                    - clientCatalog
                    - rulesTokens);

                return new ConversationTurnContextBreakdown
                {
                    TurnIndex = turn.TurnIndex,
                    SystemPromptTokensEstimated = systemPromptTokens,
                    WorkingMemoryTokensEstimated = workingMemoryTokens,
                    PreparedVirtualToolSchemaTokensEstimated = virtualCatalog,
                    PreparedClientToolSchemaTokensEstimated = clientCatalog,
                    PreparedRulesTokensEstimated = rulesTokens,
                    HistoryTokensEstimated = historyTokens
                };
            })
            .ToList();
    }

    public Task<bool> ConversationExistsAsync(Guid conversationId, CancellationToken cancellationToken) =>
        _conversationRepository.ExistsAsync(conversationId, cancellationToken);

    public async Task<ConversationSummaryDto?> GetTelemetrySummaryAsync(
        Guid conversationId,
        int? maxTurns,
        CancellationToken cancellationToken)
    {
        var rollup = await _summaryRepository.GetRollupAsync(conversationId, cancellationToken);
        if (rollup is null)
        {
            return null;
        }

        var basis = Basis;
        var take = TelemetryQueryLimits.ClampTake(maxTurns);
        // Same DbContext scope: sequential EF calls only.
        var turns = PromptTokenBasisProjector.ApplyBasis(
            await _turnMetricRepository.ListBoundedProjectionsAsync(
                conversationId,
                take,
                cancellationToken),
            basis);
        var rawFinal = await _turnMetricRepository.GetFinalTurnProjectionAsync(
            conversationId,
            cancellationToken);
        var finalTurn = rawFinal is null
            ? null
            : PromptTokenBasisProjector.ApplyBasis(rawFinal, basis);

        ConversationTurnSavingsAggregates? savingsAggregates;
        if (basis == PromptTokenBasis.ProviderActual)
        {
            var allMetrics = await _turnMetricRepository.ListByConversationIdAsync(
                conversationId,
                cancellationToken) ?? [];
            var projected = allMetrics
                .Select(m => PromptTokenBasisProjector.Project(m, basis))
                .ToList();
            savingsAggregates = projected.Count == 0
                ? null
                : new ConversationTurnSavingsAggregates
                {
                    SimpleAverageNetTokenSavingsRatio = projected.Average(p => p.NetTokenSavingsRatio),
                    PeakNetTokenSavingsRatio = projected.Max(p => p.NetTokenSavingsRatio),
                    TurnCount = projected.Count
                };

            return MapSummaryFromProjected(rollup, turns, finalTurn, savingsAggregates, projected);
        }

        savingsAggregates = await _turnMetricRepository.GetSavingsAggregatesAsync(
            conversationId,
            cancellationToken);
        return MapSummary(rollup, turns, finalTurn, savingsAggregates);
    }

    public async Task<IReadOnlyList<ConversationTurnDto>> GetTelemetryTurnsAsync(
        Guid conversationId,
        int? maxTurns,
        CancellationToken cancellationToken)
    {
        var take = TelemetryQueryLimits.ClampTake(maxTurns);
        var turns = PromptTokenBasisProjector.ApplyBasis(
            await _turnMetricRepository.ListBoundedProjectionsAsync(
                conversationId,
                take,
                cancellationToken),
            Basis);

        return turns.Select(MapTurn).ToList();
    }

    public async Task<FinalTurnSnapshotDto?> GetFinalTurnSnapshotAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var raw = await _turnMetricRepository.GetFinalTurnProjectionAsync(conversationId, cancellationToken);
        if (raw is null)
        {
            return null;
        }

        var final = PromptTokenBasisProjector.ApplyBasis(raw, Basis);
        return new FinalTurnSnapshotDto
        {
            ConversationId = conversationId,
            TurnIndex = final.TurnIndex,
            BaselineTotalTokensEstimated = final.BaselineTotalTokensEstimated,
            CompressedTotalTokensEstimated = final.CompressedTotalTokensEstimated,
            NetTokensSaved = final.NetTokensSaved,
            NetTokenSavingsRatio = final.NetTokenSavingsRatio,
            RawMessageCount = final.RawMessageCount,
            SentMessageCount = final.SentMessageCount,
            WorkingMemoryVersionUsed = final.WorkingMemoryVersionUsed,
            TrimTriggered = final.TrimTriggered
        };
    }

    public async Task<IReadOnlyList<ConversationPhaseDto>> GetPhaseBreakdownAsync(
        Guid conversationId,
        int? maxTurns,
        CancellationToken cancellationToken)
    {
        var take = TelemetryQueryLimits.ClampTake(maxTurns);
        var turns = PromptTokenBasisProjector.ApplyBasis(
            await _turnMetricRepository.ListBoundedProjectionsAsync(
                conversationId,
                take,
                cancellationToken),
            Basis);

        return PhaseCalculator.Calculate(turns);
    }

    public async Task<ConversationBudgetEventDto?> GetBudgetEventsAsync(
        Guid conversationId,
        int? maxTurns,
        CancellationToken cancellationToken)
    {
        var take = TelemetryQueryLimits.ClampTake(maxTurns);
        var turns = PromptTokenBasisProjector.ApplyBasis(
            await _turnMetricRepository.ListBoundedProjectionsAsync(
                conversationId,
                take,
                cancellationToken),
            Basis);
        if (turns.Count == 0)
        {
            return null;
        }

        return BudgetEventMapper.Map(conversationId, turns);
    }

    public async Task<PromptGrowthTimelineDto?> GetPromptGrowthTimelineAsync(
        Guid conversationId,
        int? maxTurns,
        CancellationToken cancellationToken)
    {
        var take = TelemetryQueryLimits.ClampTake(maxTurns);
        var turns = await _turnMetricRepository.ListBoundedProjectionsAsync(
            conversationId,
            take,
            cancellationToken);
        if (turns.Count == 0)
        {
            return null;
        }

        return new PromptGrowthTimelineDto
        {
            ConversationId = conversationId,
            Points = turns.Select(t => new PromptGrowthPointDto
            {
                TurnIndex = t.TurnIndex,
                ActualPromptTokens = t.ActualPromptTokens,
                CompressedInputTokensEstimated = t.CompressedInputTokensEstimated,
                EffectivePromptTokens = t.ActualPromptTokens ?? t.CompressedInputTokensEstimated
            }).ToList()
        };
    }

    public async Task<string?> GetEvidenceMarkdownAsync(
        Guid conversationId,
        int? maxTurns,
        CancellationToken cancellationToken)
    {
        var summary = await GetTelemetrySummaryAsync(conversationId, maxTurns, cancellationToken);
        var finalTurn = await GetFinalTurnSnapshotAsync(conversationId, cancellationToken);
        if (summary is null || finalTurn is null)
        {
            return null;
        }

        return _evidenceMarkdownService.Build(summary, finalTurn);
    }

    public async Task<ConversationComparisonDto?> CompareConversationsAsync(
        Guid leftConversationId,
        Guid rightConversationId,
        int? maxTurns,
        CancellationToken cancellationToken)
    {
        var left = await GetTelemetrySummaryAsync(leftConversationId, maxTurns, cancellationToken);
        var right = await GetTelemetrySummaryAsync(rightConversationId, maxTurns, cancellationToken);
        if (left is null || right is null)
        {
            return null;
        }

        return new ConversationComparisonDto
        {
            Left = left,
            Right = right
        };
    }

    private ConversationSummaryDto MapSummaryFromProjected(
        ConversationSummaryRollup rollup,
        IReadOnlyList<ConversationTurnProjection> sampleTurns,
        ConversationTurnProjection? finalTurn,
        ConversationTurnSavingsAggregates? savingsAggregates,
        IReadOnlyList<PromptTokenBasisProjector.ProjectedTurn> allProjected)
    {
        long baseline = 0;
        long compressedPromptAndCompletion = 0;
        long netExcludingOverhead = 0;
        long totalVirtualTools = 0;
        foreach (var p in allProjected)
        {
            baseline += p.BaselineTotalTokens;
            compressedPromptAndCompletion += p.CompressedTotalTokens;
            netExcludingOverhead += p.NetTokensSaved;
            totalVirtualTools += p.VirtualToolsTokensSaved ?? 0;
        }

        // Match stored rollup: TotalActual includes compression overhead; net subtracts it.
        var overhead = rollup.TotalCompressionOverheadTokens;
        var totalNet = netExcludingOverhead - overhead;
        var weighted = baseline > 0
            ? Math.Round((double)totalNet / baseline, 6)
            : 0d;

        var ratios = sampleTurns.Select(t => t.NetTokenSavingsRatio).OrderBy(r => r).ToList();
        var median = ratios.Count == 0
            ? 0d
            : ratios.Count % 2 == 1
                ? ratios[ratios.Count / 2]
                : (ratios[(ratios.Count / 2) - 1] + ratios[ratios.Count / 2]) / 2d;

        var simpleAverage = savingsAggregates is null
            ? 0d
            : Math.Round(savingsAggregates.SimpleAverageNetTokenSavingsRatio, 6);
        var peak = savingsAggregates is null
            ? 0d
            : Math.Round(savingsAggregates.PeakNetTokenSavingsRatio, 6);

        var sampleCount = sampleTurns.Count;
        return new ConversationSummaryDto
        {
            ConversationId = rollup.ConversationId,
            TurnCount = rollup.TotalTurns,
            Model = finalTurn?.Model,
            TotalBaselineTokensEstimated = baseline,
            TotalCompressedTokensEstimated = compressedPromptAndCompletion,
            TotalNetTokensSaved = totalNet,
            TotalVirtualToolsTokensSaved = totalVirtualTools,
            TotalCompressionOverheadTokens = overhead,
            WeightedSavingsRatio = weighted,
            SimpleAverageSavingsRatio = simpleAverage,
            MedianSavingsRatio = Math.Round(median, 6),
            PeakSavingsRatio = peak,
            FinalTurnSavingsRatio = Math.Round(finalTurn?.NetTokenSavingsRatio ?? 0d, 6),
            SampleTurnCount = sampleCount,
            SampleFirstTurnIndex = sampleCount == 0 ? null : sampleTurns[0].TurnIndex,
            SampleLastTurnIndex = sampleCount == 0 ? null : sampleTurns[^1].TurnIndex,
            IsPartialTurnSample = sampleCount < rollup.TotalTurns,
            SavingsRegressions = _regressionDetector.DetectSavingsRegressions(sampleTurns)
        };
    }

    private ConversationSummaryDto MapSummary(
        ConversationSummaryRollup rollup,
        IReadOnlyList<ConversationTurnProjection> sampleTurns,
        ConversationTurnProjection? finalTurn,
        ConversationTurnSavingsAggregates? savingsAggregates)
    {
        var ratios = sampleTurns.Select(t => t.NetTokenSavingsRatio).OrderBy(r => r).ToList();
        var median = ratios.Count == 0
            ? 0d
            : ratios.Count % 2 == 1
                ? ratios[ratios.Count / 2]
                : (ratios[(ratios.Count / 2) - 1] + ratios[ratios.Count / 2]) / 2d;

        var weighted = rollup.TotalBaselineTokensEstimated > 0
            ? Math.Round(
                (double)rollup.TotalNetTokensSaved / rollup.TotalBaselineTokensEstimated,
                6)
            : 0d;

        var simpleAverage = savingsAggregates is null
            ? 0d
            : Math.Round(savingsAggregates.SimpleAverageNetTokenSavingsRatio, 6);
        var peak = savingsAggregates is null
            ? 0d
            : Math.Round(savingsAggregates.PeakNetTokenSavingsRatio, 6);

        var finalRatio = finalTurn?.NetTokenSavingsRatio ?? 0d;
        var model = finalTurn?.Model;

        var compressedEquivalent =
            rollup.TotalCompressedPromptTokens + rollup.TotalCompletionTokens;

        var sampleCount = sampleTurns.Count;
        return new ConversationSummaryDto
        {
            ConversationId = rollup.ConversationId,
            TurnCount = rollup.TotalTurns,
            Model = model,
            TotalBaselineTokensEstimated = rollup.TotalBaselineTokensEstimated,
            TotalCompressedTokensEstimated = compressedEquivalent,
            TotalNetTokensSaved = rollup.TotalNetTokensSaved,
            TotalVirtualToolsTokensSaved = rollup.TotalVirtualToolsTokensSaved,
            TotalCompressionOverheadTokens = rollup.TotalCompressionOverheadTokens,
            WeightedSavingsRatio = weighted,
            SimpleAverageSavingsRatio = simpleAverage,
            MedianSavingsRatio = Math.Round(median, 6),
            PeakSavingsRatio = peak,
            FinalTurnSavingsRatio = Math.Round(finalRatio, 6),
            SampleTurnCount = sampleCount,
            SampleFirstTurnIndex = sampleCount == 0 ? null : sampleTurns[0].TurnIndex,
            SampleLastTurnIndex = sampleCount == 0 ? null : sampleTurns[^1].TurnIndex,
            IsPartialTurnSample = sampleCount < rollup.TotalTurns,
            SavingsRegressions = _regressionDetector.DetectSavingsRegressions(sampleTurns)
        };
    }

    private static ConversationTurnDto MapTurn(ConversationTurnProjection turn)
    {
        var compressionRatio = turn.BaselineTotalTokensEstimated > 0
            ? Math.Round(
                (double)turn.CompressedTotalTokensEstimated / turn.BaselineTotalTokensEstimated,
                6)
            : 0d;
        // PromptEstimateError always compares provider usage to the stored tiktoken estimate.
        // When ProviderActual rewrites CompressedInputTokensEstimated, use Actual when both match
        // would zero the error — keep the stored estimate via Actual - (Actual when projected).
        // After ApplyBasis, CompressedInputTokensEstimated may equal Actual; error is then 0, which
        // is correct for the projected view. For Estimated basis, this is actual - estimate.
        int? promptEstimateError = turn.ActualPromptTokens.HasValue
            ? turn.ActualPromptTokens.Value - turn.CompressedInputTokensEstimated
            : null;
        double? messageReductionRatio = turn.RawMessageCount > 0
            ? Math.Round(1d - ((double)turn.SentMessageCount / turn.RawMessageCount), 6)
            : null;

        return new ConversationTurnDto
        {
            TurnIndex = turn.TurnIndex,
            RequestStartedAt = turn.RequestStartedAt,
            Model = turn.Model,
            RawInputTokensEstimated = turn.RawInputTokensEstimated,
            IrFullInputTokensEstimated = turn.IrFullInputTokensEstimated,
            CompressedInputTokensEstimated = turn.CompressedInputTokensEstimated,
            ActualPromptTokens = turn.ActualPromptTokens,
            ActualCompletionTokens = turn.ActualCompletionTokens,
            BaselineTotalTokensEstimated = turn.BaselineTotalTokensEstimated,
            CompressedTotalTokensEstimated = turn.CompressedTotalTokensEstimated,
            NetTokensSaved = turn.NetTokensSaved,
            NetTokenSavingsRatio = turn.NetTokenSavingsRatio,
            VirtualToolsTokensSaved = turn.VirtualToolsTokensSaved,
            IsLegacyMixedAxis = turn.IrFullInputTokensEstimated is null,
            CompressionRatio = compressionRatio,
            PromptEstimateError = promptEstimateError,
            MessageReductionRatio = messageReductionRatio,
            SoftBudgetExceeded = turn.SoftBudgetExceeded,
            HardBudgetExceeded = turn.HardBudgetExceeded,
            TrimTriggered = turn.TrimTriggered,
            WorkingMemoryVersionUsed = turn.WorkingMemoryVersionUsed,
            RawMessageCount = turn.RawMessageCount,
            SentMessageCount = turn.SentMessageCount,
            DurationMs = turn.DurationMs,
            UpstreamDurationMs = turn.UpstreamDurationMs,
            PrepareDurationMs = turn.PrepareDurationMs,
            CreatedAt = turn.CreatedAt
        };
    }

    internal static class PhaseCalculator
    {
        public static IReadOnlyList<ConversationPhaseDto> Calculate(
            IReadOnlyList<ConversationTurnProjection> turns)
        {
            if (turns.Count == 0)
            {
                return [];
            }

            var phases = new List<ConversationPhaseDto>();
            var phaseStart = 0;
            long baselineSum = turns[0].BaselineTotalTokensEstimated;
            long netSavedSum = turns[0].NetTokensSaved;

            for (var i = 1; i < turns.Count; i++)
            {
                var previous = turns[i - 1];
                var current = turns[i];
                var boundary =
                    previous.WorkingMemoryVersionUsed != current.WorkingMemoryVersionUsed
                    || previous.TrimTriggered != current.TrimTriggered;

                if (boundary)
                {
                    phases.Add(BuildPhase(turns[phaseStart], turns[i - 1], baselineSum, netSavedSum));
                    phaseStart = i;
                    baselineSum = current.BaselineTotalTokensEstimated;
                    netSavedSum = current.NetTokensSaved;
                }
                else
                {
                    baselineSum += current.BaselineTotalTokensEstimated;
                    netSavedSum += current.NetTokensSaved;
                }
            }

            phases.Add(BuildPhase(turns[phaseStart], turns[^1], baselineSum, netSavedSum));
            return phases;
        }

        private static ConversationPhaseDto BuildPhase(
            ConversationTurnProjection start,
            ConversationTurnProjection end,
            long baselineSum,
            long netSavedSum)
        {
            var weighted = baselineSum > 0
                ? Math.Round((double)netSavedSum / baselineSum, 6)
                : 0d;

            return new ConversationPhaseDto
            {
                Phase = NamePhase(start.WorkingMemoryVersionUsed, start.TrimTriggered),
                TurnStart = start.TurnIndex,
                TurnEnd = end.TurnIndex,
                WorkingMemoryVersionUsed = start.WorkingMemoryVersionUsed,
                TrimTriggered = start.TrimTriggered,
                TotalBaselineTokensEstimated = baselineSum,
                TotalNetTokensSaved = netSavedSum,
                WeightedSavingsRatio = weighted
            };
        }

        private static string NamePhase(int? workingMemoryVersion, bool trimTriggered)
        {
            if (trimTriggered)
            {
                return "trimmed_mature_state";
            }

            if (workingMemoryVersion is null)
            {
                return "early_pre_working_memory";
            }

            return $"working_memory_v{workingMemoryVersion}";
        }
    }

    internal static class BudgetEventMapper
    {
        public static ConversationBudgetEventDto Map(
            Guid conversationId,
            IReadOnlyList<ConversationTurnProjection> turns)
        {
            int? softFirst = null;
            int? hardFirst = null;
            int? trimFirst = null;
            int? maxPrompt = null;
            int? maxPromptTurn = null;
            int? postTrimPrompt = null;

            foreach (var turn in turns)
            {
                if (softFirst is null && turn.SoftBudgetExceeded)
                {
                    softFirst = turn.TurnIndex;
                }

                if (hardFirst is null && turn.HardBudgetExceeded)
                {
                    hardFirst = turn.TurnIndex;
                }

                if (trimFirst is null && turn.TrimTriggered)
                {
                    trimFirst = turn.TurnIndex;
                }

                var effectivePrompt = turn.ActualPromptTokens ?? turn.CompressedInputTokensEstimated;
                if (maxPrompt is null || effectivePrompt > maxPrompt)
                {
                    maxPrompt = effectivePrompt;
                    maxPromptTurn = turn.TurnIndex;
                }
            }

            // First turn after trim; null when trim is the last sampled turn.
            if (trimFirst is not null)
            {
                var postTrimTurn = turns.FirstOrDefault(t => t.TurnIndex > trimFirst.Value);
                if (postTrimTurn is not null)
                {
                    postTrimPrompt =
                        postTrimTurn.ActualPromptTokens ?? postTrimTurn.CompressedInputTokensEstimated;
                }
            }

            return new ConversationBudgetEventDto
            {
                ConversationId = conversationId,
                SoftBudgetFirstExceededAtTurn = softFirst,
                HardBudgetFirstExceededAtTurn = hardFirst,
                TrimFirstTriggeredAtTurn = trimFirst,
                MaxActualPromptTokens = maxPrompt,
                MaxActualPromptTokensTurn = maxPromptTurn,
                PostTrimActualPromptTokens = postTrimPrompt
            };
        }
    }
}
