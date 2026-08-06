using Comprexy.Application.Abstractions;
using Comprexy.Application.Services;
using Comprexy.ControlApi.Contracts.Metrics;
using Comprexy.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Comprexy.ControlApi.Endpoints;

public static class MetricsEndpoints
{
    public static IEndpointRouteBuilder MapMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/comprexy")
            .WithTags("ComprexyMetrics");

        group.MapGet("/conversations", ListConversationsAsync);
        group.MapGet("/conversations/{conversationId:guid}/metrics", GetConversationMetricsAsync);
        group.MapGet("/conversations/{conversationId:guid}/metrics/turns", ListTurnMetricsAsync);

        return app;
    }

    private static async Task<IResult> ListConversationsAsync(
        PromptTokenBasis? promptTokenBasis,
        [FromServices] PromptTokenBasisContext basisContext,
        IConversationMetricsQueryService metricsQuery,
        CancellationToken cancellationToken)
    {
        ApplyBasisOverride(basisContext, promptTokenBasis);
        var items = await metricsQuery.ListConversationSummariesAsync(cancellationToken);
        if (basisContext.Resolve() == PromptTokenBasis.Estimated)
        {
            return TypedResults.Ok(items.Select(ConversationMetricsMapper.ToListItem).ToList());
        }

        var projected = new List<ConversationMetricsListItemDto>(items.Count);
        foreach (var summary in items)
        {
            var turns = await metricsQuery.ListTurnMetricsAsync(summary.ConversationId, cancellationToken);
            projected.Add(ConversationMetricsMapper.ToListItem(summary, turns, PromptTokenBasis.ProviderActual));
        }

        return TypedResults.Ok(projected);
    }

    private static async Task<IResult> GetConversationMetricsAsync(
        Guid conversationId,
        PromptTokenBasis? promptTokenBasis,
        [FromServices] PromptTokenBasisContext basisContext,
        IConversationMetricsQueryService metricsQuery,
        [FromServices] IConversationRepository conversationRepository,
        CancellationToken cancellationToken)
    {
        ApplyBasisOverride(basisContext, promptTokenBasis);
        var summary = await metricsQuery.GetConversationSummaryAsync(conversationId, cancellationToken);
        if (summary is null)
        {
            return TypedResults.NotFound();
        }

        var conversation = await conversationRepository.FindByIdAsync(conversationId, cancellationToken);
        var effectiveSettingsJson = conversation?.EffectiveSettingsJson;

        if (basisContext.Resolve() == PromptTokenBasis.Estimated)
        {
            return TypedResults.Ok(
                ConversationMetricsMapper.ToSummaryDto(
                    summary,
                    turns: null,
                    PromptTokenBasis.Estimated,
                    effectiveSettingsJson));
        }

        var turns = await metricsQuery.ListTurnMetricsAsync(conversationId, cancellationToken);
        return TypedResults.Ok(
            ConversationMetricsMapper.ToSummaryDto(
                summary,
                turns,
                PromptTokenBasis.ProviderActual,
                effectiveSettingsJson));
    }

    private static async Task<IResult> ListTurnMetricsAsync(
        Guid conversationId,
        PromptTokenBasis? promptTokenBasis,
        [FromServices] PromptTokenBasisContext basisContext,
        IConversationMetricsQueryService metricsQuery,
        CancellationToken cancellationToken)
    {
        ApplyBasisOverride(basisContext, promptTokenBasis);
        var summary = await metricsQuery.GetConversationSummaryAsync(conversationId, cancellationToken);
        if (summary is null)
        {
            return TypedResults.NotFound();
        }

        var turns = await metricsQuery.ListTurnMetricsAsync(conversationId, cancellationToken);
        var breakdowns = await metricsQuery.ListTurnContextBreakdownsAsync(conversationId, cancellationToken);
        var breakdownsByTurn = breakdowns.ToDictionary(b => b.TurnIndex);
        var basis = basisContext.Resolve();

        var dto = turns
            .Select(turn => ConversationMetricsMapper.ToTurnDto(
                turn,
                breakdownsByTurn.GetValueOrDefault(turn.TurnIndex),
                basis))
            .ToList();
        return TypedResults.Ok(dto);
    }

    private static void ApplyBasisOverride(
        PromptTokenBasisContext basisContext,
        PromptTokenBasis? promptTokenBasis)
    {
        if (promptTokenBasis is not null)
        {
            basisContext.RequestOverride = promptTokenBasis;
        }
    }
}
