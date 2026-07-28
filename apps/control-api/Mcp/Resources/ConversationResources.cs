using System.ComponentModel;
using Comprexy.Application.Abstractions;
using Comprexy.ControlApi.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Comprexy.ControlApi.Mcp.Resources;

[McpServerResourceType]
public sealed class ConversationResources(
    IConversationMetricsQueryService metricsQuery,
    IOptions<McpTelemetryOptions> options)
{
    [McpServerResource(UriTemplate = "comprexy://conversation/{conversationId}/summary", Name = "conversation_summary", MimeType = "application/json")]
    [Description("Conversation summary by id.")]
    public Task<string> GetSummaryAsync(Guid conversationId, CancellationToken cancellationToken) =>
        ReadAsync(
            conversationId,
            async (id, take, ct) =>
            {
                if (!await metricsQuery.ConversationExistsAsync(id, ct))
                {
                    return McpTelemetryHelper.ErrorJson($"Conversation not found: {id}");
                }

                var summary = await metricsQuery.GetTelemetrySummaryAsync(id, take, ct);
                return summary is null
                    ? McpTelemetryHelper.NotFoundJson(id)
                    : McpTelemetryHelper.ToJson(summary);
            },
            cancellationToken);

    [McpServerResource(UriTemplate = "comprexy://conversation/{conversationId}/turns", Name = "conversation_turns", MimeType = "application/json")]
    [Description("Per-turn metrics by conversation id.")]
    public Task<string> GetTurnsAsync(Guid conversationId, CancellationToken cancellationToken) =>
        ReadAsync(
            conversationId,
            async (id, take, ct) =>
            {
                if (!await metricsQuery.ConversationExistsAsync(id, ct))
                {
                    return McpTelemetryHelper.ErrorJson($"Conversation not found: {id}");
                }

                var turns = await metricsQuery.GetTelemetryTurnsAsync(id, take, ct);
                return McpTelemetryHelper.ToJson(turns);
            },
            cancellationToken);

    [McpServerResource(UriTemplate = "comprexy://conversation/{conversationId}/phases", Name = "conversation_phases", MimeType = "application/json")]
    [Description("Compression phase breakdown by conversation id.")]
    public Task<string> GetPhasesAsync(Guid conversationId, CancellationToken cancellationToken) =>
        ReadAsync(
            conversationId,
            async (id, take, ct) =>
            {
                if (!await metricsQuery.ConversationExistsAsync(id, ct))
                {
                    return McpTelemetryHelper.ErrorJson($"Conversation not found: {id}");
                }

                return McpTelemetryHelper.ToJson(await metricsQuery.GetPhaseBreakdownAsync(id, take, ct));
            },
            cancellationToken);

    [McpServerResource(UriTemplate = "comprexy://conversation/{conversationId}/final-turn", Name = "conversation_final_turn", MimeType = "application/json")]
    [Description("Final turn snapshot by conversation id.")]
    public Task<string> GetFinalTurnAsync(Guid conversationId, CancellationToken cancellationToken) =>
        ReadAsync(
            conversationId,
            async (id, _, ct) =>
            {
                if (!await metricsQuery.ConversationExistsAsync(id, ct))
                {
                    return McpTelemetryHelper.ErrorJson($"Conversation not found: {id}");
                }

                var snapshot = await metricsQuery.GetFinalTurnSnapshotAsync(id, ct);
                return snapshot is null
                    ? McpTelemetryHelper.NotFoundJson(id)
                    : McpTelemetryHelper.ToJson(snapshot);
            },
            cancellationToken);

    [McpServerResource(UriTemplate = "comprexy://conversation/{conversationId}/budget-events", Name = "conversation_budget_events", MimeType = "application/json")]
    [Description("Budget events by conversation id.")]
    public Task<string> GetBudgetEventsAsync(Guid conversationId, CancellationToken cancellationToken) =>
        ReadAsync(
            conversationId,
            async (id, take, ct) =>
            {
                if (!await metricsQuery.ConversationExistsAsync(id, ct))
                {
                    return McpTelemetryHelper.ErrorJson($"Conversation not found: {id}");
                }

                var events = await metricsQuery.GetBudgetEventsAsync(id, take, ct);
                return events is null
                    ? McpTelemetryHelper.NotFoundJson(id)
                    : McpTelemetryHelper.ToJson(events);
            },
            cancellationToken);

    [McpServerResource(UriTemplate = "comprexy://conversation/{conversationId}/evidence", Name = "conversation_evidence", MimeType = "text/markdown")]
    [Description("Evidence markdown by conversation id.")]
    public Task<string> GetEvidenceAsync(Guid conversationId, CancellationToken cancellationToken) =>
        ReadAsync(
            conversationId,
            async (id, take, ct) =>
            {
                if (!await metricsQuery.ConversationExistsAsync(id, ct))
                {
                    return McpTelemetryHelper.ErrorJson($"Conversation not found: {id}");
                }

                var markdown = await metricsQuery.GetEvidenceMarkdownAsync(id, take, ct);
                return markdown ?? McpTelemetryHelper.NotFoundJson(id);
            },
            cancellationToken);

    [McpServerResource(UriTemplate = "comprexy://conversation/{conversationId}/prompt-growth-timeline", Name = "conversation_prompt_growth_timeline", MimeType = "application/json")]
    [Description("Prompt growth timeline by conversation id.")]
    public Task<string> GetPromptGrowthTimelineAsync(Guid conversationId, CancellationToken cancellationToken) =>
        ReadAsync(
            conversationId,
            async (id, take, ct) =>
            {
                if (!await metricsQuery.ConversationExistsAsync(id, ct))
                {
                    return McpTelemetryHelper.ErrorJson($"Conversation not found: {id}");
                }

                var timeline = await metricsQuery.GetPromptGrowthTimelineAsync(id, take, ct);
                return timeline is null
                    ? McpTelemetryHelper.NotFoundJson(id)
                    : McpTelemetryHelper.ToJson(timeline);
            },
            cancellationToken);

    private async Task<string> ReadAsync(
        Guid conversationId,
        Func<Guid, int, CancellationToken, Task<string>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = McpTelemetryHelper.CreateTimeoutCts(options, cancellationToken);
            var take = McpTelemetryHelper.ResolveTake(options);
            return await action(conversationId, take, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return McpTelemetryHelper.ErrorJson("Telemetry query timed out.");
        }
        catch (Exception ex)
        {
            return McpTelemetryHelper.ErrorJson($"Telemetry query failed: {ex.Message}");
        }
    }
}
