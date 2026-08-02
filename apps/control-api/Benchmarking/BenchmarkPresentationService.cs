using System.Text.Json;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Models.Benchmarking;
using Comprexy.ControlApi.Contracts.Benchmark;
using Comprexy.Domain.Entities;

namespace Comprexy.ControlApi.Benchmarking;

public sealed class BenchmarkPresentationService
{
    private static readonly JsonSerializerOptions ArtifactJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IConversationMetricsQueryService _metricsQuery;
    private readonly IBenchmarkTotalsCalculator _totalsCalculator;
    private readonly IBenchmarkCostCalculator _costCalculator;

    public BenchmarkPresentationService(
        IConversationMetricsQueryService metricsQuery,
        IBenchmarkTotalsCalculator totalsCalculator,
        IBenchmarkCostCalculator costCalculator)
    {
        _metricsQuery = metricsQuery;
        _totalsCalculator = totalsCalculator;
        _costCalculator = costCalculator;
    }

    public async Task<BenchmarkComparisonPresentationResponse?> BuildRunPresentationAsync(
        string runDirectory,
        CancellationToken cancellationToken)
    {
        var presentationPath = Path.Combine(runDirectory, "presentation.json");
        if (!File.Exists(presentationPath))
        {
            return null;
        }

        await using var presentationStream = File.OpenRead(presentationPath);
        var presentation = await JsonSerializer.DeserializeAsync<BenchPresentationArtifact>(
            presentationStream,
            ArtifactJsonOptions,
            cancellationToken);
        if (presentation?.Metrics?.Paired is not { Count: > 0 } paired)
        {
            return null;
        }

        var manifestPath = Path.Combine(runDirectory, "manifest.json");
        BenchManifestArtifact? manifest = null;
        if (File.Exists(manifestPath))
        {
            await using var manifestStream = File.OpenRead(manifestPath);
            manifest = await JsonSerializer.DeserializeAsync<BenchManifestArtifact>(
                manifestStream,
                ArtifactJsonOptions,
                cancellationToken);
        }

        var orderedPairs = paired
            .Where(p => p.MafCompact is not null && p.Comprexy is not null)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
        if (orderedPairs.Count == 0)
        {
            return null;
        }

        var baselineTotals = AggregateArmTotals(orderedPairs, p => p.MafCompact!);
        var compareTotals = AggregateArmTotals(orderedPairs, p => p.Comprexy!);
        var comparisonTotals = _totalsCalculator.Compare(baselineTotals, compareTotals);

        var pairCaveats = orderedPairs
            .SelectMany(p => p.Caveats ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (pairCaveats.Count > 0)
        {
            comparisonTotals = comparisonTotals with
            {
                Caveats = comparisonTotals.Caveats.Concat(pairCaveats).Distinct(StringComparer.Ordinal).ToList()
            };
        }

        var rates = NormalizeRates(
            presentation.CostRates ?? manifest?.CostRates,
            (presentation.CostRates ?? manifest?.CostRates)?.ModelKind ?? BenchmarkModelKind.Local);

        var (baselineId, compareId) = ResolveConversationIds(orderedPairs, manifest);

        return new BenchmarkComparisonPresentationResponse
        {
            Totals = comparisonTotals,
            Cost = rates.ModelKind == BenchmarkModelKind.Usd
                ? _costCalculator.ComputeComparisonCost(comparisonTotals, rates)
                : null,
            BaselineConversationId = baselineId,
            CompareConversationId = compareId,
            RunId = presentation.RunId,
            TurnSeriesPaths = presentation.TurnSeriesPaths ?? []
        };
    }

    public async Task<BenchmarkTelemetryPresentationResponse?> BuildTelemetryAsync(
        BenchmarkTelemetryPresentationRequest request,
        CancellationToken cancellationToken)
    {
        var totals = await LoadTotalsAsync(request.ConversationId, cancellationToken);
        if (totals is null)
        {
            return null;
        }

        var rates = NormalizeRates(request.Rates, request.ModelKind);
        return new BenchmarkTelemetryPresentationResponse
        {
            Totals = totals,
            Cost = rates.ModelKind == BenchmarkModelKind.Usd
                ? _costCalculator.ComputeTelemetryCost(totals, rates)
                : null
        };
    }

    public async Task<BenchmarkComparisonPresentationResponse?> BuildComparisonAsync(
        BenchmarkComparisonPresentationRequest request,
        CancellationToken cancellationToken)
    {
        var baseline = await LoadTotalsAsync(request.BaselineConversationId, cancellationToken);
        var compare = await LoadTotalsAsync(request.CompareConversationId, cancellationToken);
        if (baseline is null || compare is null)
        {
            return null;
        }

        var totals = _totalsCalculator.Compare(baseline, compare);
        var rates = NormalizeRates(request.Rates, request.ModelKind);
        return new BenchmarkComparisonPresentationResponse
        {
            Totals = totals,
            Cost = rates.ModelKind == BenchmarkModelKind.Usd
                ? _costCalculator.ComputeComparisonCost(totals, rates)
                : null
        };
    }

    private async Task<ConversationTokenTotals?> LoadTotalsAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var summary = await _metricsQuery.GetConversationSummaryAsync(conversationId, cancellationToken);
        if (summary is null || summary.TotalTurns == 0)
        {
            return null;
        }

        var turns = await _metricsQuery.ListTurnMetricsAsync(conversationId, cancellationToken);
        return _totalsCalculator.FromSummary(
            conversationId,
            summary.TotalTurns,
            summary.TotalCompressedPromptTokens,
            summary.TotalCompletionTokens,
            summary.TotalCompressionOverheadTokens,
            wallClockMs: null,
            SumDurations(turns, t => t.DurationMs),
            SumDurations(turns, t => t.UpstreamDurationMs),
            SumDurations(turns, t => t.PrepareDurationMs));
    }

    private static long? SumDurations(
        IReadOnlyList<ConversationTurnMetric> turns,
        Func<ConversationTurnMetric, int?> selector)
    {
        var values = turns.Select(selector).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return values.Count == 0 ? null : values.Sum(v => (long)v);
    }

    private static ConversationTokenTotals AggregateArmTotals(
        IReadOnlyList<BenchPairedConversationArtifact> pairs,
        Func<BenchPairedConversationArtifact, BenchConversationMetricsArtifact> selector)
    {
        var metrics = pairs.Select(selector).ToList();
        var primary = metrics[0];
        return new ConversationTokenTotals
        {
            ConversationId = primary.ConversationId,
            TurnCount = metrics.Sum(m => m.TurnCount),
            InputTokens = metrics.Sum(m => m.InputTokens),
            OutputTokens = metrics.Sum(m => m.OutputTokens),
            OverheadTokens = metrics.Sum(m => m.CompressionOverheadTokens),
            WallClockMs = metrics.Sum(m => m.ConversationWallClockMs),
            TotalProxyDurationMs = SumNullable(metrics.Select(m => m.TotalProxyTurnDurationMs)),
            TotalUpstreamDurationMs = SumNullable(metrics.Select(m => m.TotalUpstreamDurationMs)),
            TotalPrepareDurationMs = SumNullable(metrics.Select(m => m.TotalPrepareDurationMs))
        };
    }

    private static (Guid? BaselineId, Guid? CompareId) ResolveConversationIds(
        IReadOnlyList<BenchPairedConversationArtifact> orderedPairs,
        BenchManifestArtifact? manifest)
    {
        var first = orderedPairs[0];
        var baselineId = first.MafCompact?.ConversationId;
        var compareId = first.Comprexy?.ConversationId;

        if (baselineId is not null && compareId is not null)
        {
            return (baselineId, compareId);
        }

        if (manifest?.Arms is null)
        {
            return (baselineId, compareId);
        }

        var baselineArm = manifest.Arms.FirstOrDefault(a =>
            string.Equals(a.Name, BenchArmNames.MafCompact, StringComparison.Ordinal));
        var compareArm = manifest.Arms.FirstOrDefault(a =>
            string.Equals(a.Name, BenchArmNames.Comprexy, StringComparison.Ordinal));
        var scriptName = first.Name;

        baselineId ??= baselineArm?.Conversations?
            .FirstOrDefault(c => string.Equals(c.Name, scriptName, StringComparison.Ordinal))
            ?.ConversationId;
        compareId ??= compareArm?.Conversations?
            .FirstOrDefault(c => string.Equals(c.Name, scriptName, StringComparison.Ordinal))
            ?.ConversationId;

        return (baselineId, compareId);
    }

    private static long? SumNullable(IEnumerable<long?> values)
    {
        var present = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return present.Count == 0 ? null : present.Sum();
    }

    private static BenchmarkCostRates NormalizeRates(BenchmarkCostRates? rates, BenchmarkModelKind modelKind)
    {
        var resolved = rates ?? BenchmarkCostRates.LocalDefaults();
        if (modelKind != BenchmarkModelKind.Local)
        {
            resolved = resolved with { ModelKind = modelKind };
        }

        return resolved.WithCompressionDefaultsFromMain();
    }
}
