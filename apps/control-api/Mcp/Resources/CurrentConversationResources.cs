using System.ComponentModel;
using Comprexy.Application.Abstractions;
using Comprexy.ControlApi.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Comprexy.ControlApi.Mcp.Resources;

[McpServerResourceType]
public sealed class CurrentConversationResources(
    IConversationMetricsQueryService metricsQuery,
    CurrentConversationResolver resolver,
    IOptions<McpTelemetryOptions> options)
{
    [McpServerResource(UriTemplate = "comprexy://current/summary", Name = "current_summary", MimeType = "application/json")]
    [Description("Current conversation summary (requires X-Comprexy-Conversation-Id).")]
    public Task<string> GetSummaryAsync(CancellationToken cancellationToken) =>
        ReadAsync(
            async (id, take, ct) =>
            {
                var summary = await metricsQuery.GetTelemetrySummaryAsync(id, take, ct);
                return summary is null
                    ? McpTelemetryHelper.NotFoundJson(id)
                    : McpTelemetryHelper.ToJson(summary);
            },
            cancellationToken);

    [McpServerResource(UriTemplate = "comprexy://current/phases", Name = "current_phases", MimeType = "application/json")]
    [Description("Current conversation compression phases (requires X-Comprexy-Conversation-Id).")]
    public Task<string> GetPhasesAsync(CancellationToken cancellationToken) =>
        ReadAsync(
            async (id, take, ct) =>
                McpTelemetryHelper.ToJson(await metricsQuery.GetPhaseBreakdownAsync(id, take, ct)),
            cancellationToken);

    [McpServerResource(UriTemplate = "comprexy://current/final-turn", Name = "current_final_turn", MimeType = "application/json")]
    [Description("Current conversation final turn snapshot (requires X-Comprexy-Conversation-Id).")]
    public Task<string> GetFinalTurnAsync(CancellationToken cancellationToken) =>
        ReadAsync(
            async (id, _, ct) =>
            {
                var snapshot = await metricsQuery.GetFinalTurnSnapshotAsync(id, ct);
                return snapshot is null
                    ? McpTelemetryHelper.NotFoundJson(id)
                    : McpTelemetryHelper.ToJson(snapshot);
            },
            cancellationToken);

    [McpServerResource(UriTemplate = "comprexy://current/budget-events", Name = "current_budget_events", MimeType = "application/json")]
    [Description("Current conversation budget events (requires X-Comprexy-Conversation-Id).")]
    public Task<string> GetBudgetEventsAsync(CancellationToken cancellationToken) =>
        ReadAsync(
            async (id, take, ct) =>
            {
                var events = await metricsQuery.GetBudgetEventsAsync(id, take, ct);
                return events is null
                    ? McpTelemetryHelper.NotFoundJson(id)
                    : McpTelemetryHelper.ToJson(events);
            },
            cancellationToken);

    [McpServerResource(UriTemplate = "comprexy://current/evidence", Name = "current_evidence", MimeType = "text/markdown")]
    [Description("Current conversation evidence markdown (requires X-Comprexy-Conversation-Id).")]
    public Task<string> GetEvidenceAsync(CancellationToken cancellationToken) =>
        ReadAsync(
            async (id, take, ct) =>
            {
                var markdown = await metricsQuery.GetEvidenceMarkdownAsync(id, take, ct);
                return markdown ?? McpTelemetryHelper.NotFoundJson(id);
            },
            cancellationToken);

    [McpServerResource(UriTemplate = "comprexy://current/prompt-growth-timeline", Name = "current_prompt_growth_timeline", MimeType = "application/json")]
    [Description("Current conversation prompt growth timeline (requires X-Comprexy-Conversation-Id).")]
    public Task<string> GetPromptGrowthTimelineAsync(CancellationToken cancellationToken) =>
        ReadAsync(
            async (id, take, ct) =>
            {
                var timeline = await metricsQuery.GetPromptGrowthTimelineAsync(id, take, ct);
                return timeline is null
                    ? McpTelemetryHelper.NotFoundJson(id)
                    : McpTelemetryHelper.ToJson(timeline);
            },
            cancellationToken);

    private async Task<string> ReadAsync(
        Func<Guid, int, CancellationToken, Task<string>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = McpTelemetryHelper.CreateTimeoutCts(options, cancellationToken);
            var resolve = await resolver.ResolveAsync(timeoutCts.Token);
            if (resolve.IsError)
            {
                return McpTelemetryHelper.ErrorJson(resolve.ErrorMessage!);
            }

            var take = McpTelemetryHelper.ResolveTake(options);
            return await action(resolve.ConversationId!.Value, take, timeoutCts.Token);
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
