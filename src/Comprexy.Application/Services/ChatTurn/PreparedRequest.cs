using System.Diagnostics;
using Comprexy.Application.Models;
using Comprexy.Domain.Entities;
using Comprexy.Application.Services.Rules;

namespace Comprexy.Application.Services.ChatTurn;

public enum InlineWrapUpMode
{
    StopTurn,
    MidChainPrefix
}

/// <summary>
/// Phase clocks for the current turn. <c>TurnStartedTimestamp</c> is a
/// <see cref="Stopwatch"/> tick so the total can be read at the metric write.
/// </summary>
public sealed record TurnPhaseTiming(
    long TurnStartedTimestamp,
    TimeSpan Prepare,
    TimeSpan Upstream)
{
    public TurnTimings ToTurnTimings() => new(
        ToMilliseconds(Prepare),
        ToMilliseconds(Upstream),
        ToMilliseconds(Stopwatch.GetElapsedTime(TurnStartedTimestamp)));

    private static int ToMilliseconds(TimeSpan elapsed) =>
        (int)Math.Clamp(Math.Round(elapsed.TotalMilliseconds), 0d, int.MaxValue);
}

public sealed record PreparedRequest(
    Conversation Conversation,
    int NextSequence,
    int EstimatedTokens,
    ContextBudgetDecision Decision,
    ProviderEndpoint Endpoint,
    UpstreamRequest UpstreamRequest,
    bool SkipCompression,
    int IncomingMessageCount,
    int? WindowStartSequence,
    int? WindowEndSequence,
    int RecentRawCount,
    ToolSchemaPrepareResult? ToolSchema = null,
    TurnMetricsPrepareData? MetricsPrepare = null,
    bool InlineFollowUpEligible = false,
    bool InlineOpenStoreEmergency = false,
    int PreFollowUpEstimatedTokens = 0,
    RulesSnapshot? RulesSnapshot = null);
