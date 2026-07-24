using Comprexy.ControlApi.Contracts.Metrics;
using Comprexy.Application.Abstractions;

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
        IConversationMetricsQueryService metricsQuery,
        CancellationToken cancellationToken)
    {
        var items = await metricsQuery.ListConversationSummariesAsync(cancellationToken);
        var dto = items.Select(ConversationMetricsMapper.ToListItem).ToList();
        return TypedResults.Ok(dto);
    }

    private static async Task<IResult> GetConversationMetricsAsync(
        Guid conversationId,
        IConversationMetricsQueryService metricsQuery,
        CancellationToken cancellationToken)
    {
        var summary = await metricsQuery.GetConversationSummaryAsync(conversationId, cancellationToken);
        if (summary is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(ConversationMetricsMapper.ToSummaryDto(summary));
    }

    private static async Task<IResult> ListTurnMetricsAsync(
        Guid conversationId,
        IConversationMetricsQueryService metricsQuery,
        CancellationToken cancellationToken)
    {
        var summary = await metricsQuery.GetConversationSummaryAsync(conversationId, cancellationToken);
        if (summary is null)
        {
            return TypedResults.NotFound();
        }

        var turns = await metricsQuery.ListTurnMetricsAsync(conversationId, cancellationToken);
        var dto = turns.Select(ConversationMetricsMapper.ToTurnDto).ToList();
        return TypedResults.Ok(dto);
    }
}
