using System.ComponentModel;
using Comprexy.Application.Abstractions;
using Comprexy.ControlApi.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Comprexy.ControlApi.Mcp.Tools;

[McpServerToolType]
public sealed class CurrentConversationTools(
    IConversationMetricsQueryService metricsQuery,
    CurrentConversationResolver resolver,
    McpToolCallAuditLogger auditLogger,
    IOptions<McpTelemetryOptions> options,
    IHttpContextAccessor httpContextAccessor)
{
    [McpServerTool(Name = "get_current_conversation_summary"), Description("Aggregate metrics for the active conversation (requires X-Comprexy-Conversation-Id).")]
    public Task<string> GetCurrentConversationSummaryAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(
            "get_current_conversation_summary",
            async (conversationId, take, ct) =>
            {
                var summary = await metricsQuery.GetTelemetrySummaryAsync(conversationId, take, ct);
                return summary is null
                    ? McpTelemetryHelper.NotFound(conversationId)
                    : McpTelemetryHelper.OkJson(summary, rowCount: 1);
            },
            cancellationToken);

    [McpServerTool(Name = "get_current_final_turn_snapshot"), Description("Final turn token proof for the active conversation (requires X-Comprexy-Conversation-Id).")]
    public Task<string> GetCurrentFinalTurnSnapshotAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(
            "get_current_final_turn_snapshot",
            async (conversationId, _, ct) =>
            {
                var snapshot = await metricsQuery.GetFinalTurnSnapshotAsync(conversationId, ct);
                return snapshot is null
                    ? McpTelemetryHelper.NotFound(conversationId)
                    : McpTelemetryHelper.OkJson(snapshot, rowCount: 1);
            },
            cancellationToken);

    [McpServerTool(Name = "get_current_compression_phase_breakdown"), Description("Compression phase breakdown for the active conversation (requires X-Comprexy-Conversation-Id).")]
    public Task<string> GetCurrentCompressionPhaseBreakdownAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(
            "get_current_compression_phase_breakdown",
            async (conversationId, take, ct) =>
            {
                var phases = await metricsQuery.GetPhaseBreakdownAsync(conversationId, take, ct);
                return McpTelemetryHelper.OkJson(phases, rowCount: phases.Count);
            },
            cancellationToken);

    [McpServerTool(Name = "get_current_budget_events"), Description("Budget and trim events for the active conversation (requires X-Comprexy-Conversation-Id).")]
    public Task<string> GetCurrentBudgetEventsAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(
            "get_current_budget_events",
            async (conversationId, take, ct) =>
            {
                var events = await metricsQuery.GetBudgetEventsAsync(conversationId, take, ct);
                return events is null
                    ? McpTelemetryHelper.NotFound(conversationId)
                    : McpTelemetryHelper.OkJson(events, rowCount: 1);
            },
            cancellationToken);

    [McpServerTool(Name = "get_current_evidence_markdown"), Description("Commit-ready evidence markdown for the active conversation (requires X-Comprexy-Conversation-Id).")]
    public Task<string> GetCurrentEvidenceMarkdownAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(
            "get_current_evidence_markdown",
            async (conversationId, take, ct) =>
            {
                var markdown = await metricsQuery.GetEvidenceMarkdownAsync(conversationId, take, ct);
                return markdown is null
                    ? McpTelemetryHelper.NotFound(conversationId)
                    : McpTelemetryHelper.OkText(markdown, rowCount: 1);
            },
            cancellationToken);

    [McpServerTool(Name = "get_current_prompt_growth_timeline"), Description("Actual prompt tokens per turn for the active conversation (requires X-Comprexy-Conversation-Id).")]
    public Task<string> GetCurrentPromptGrowthTimelineAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(
            "get_current_prompt_growth_timeline",
            async (conversationId, take, ct) =>
            {
                var timeline = await metricsQuery.GetPromptGrowthTimelineAsync(conversationId, take, ct);
                return timeline is null
                    ? McpTelemetryHelper.NotFound(conversationId)
                    : McpTelemetryHelper.OkJson(timeline, rowCount: timeline.Points.Count);
            },
            cancellationToken);

    private async Task<string> ExecuteAsync(
        string toolName,
        Func<Guid, int, CancellationToken, Task<McpToolResult>> action,
        CancellationToken cancellationToken)
    {
        var sw = McpToolCallAuditLogger.StartTimer();
        Guid? conversationId = null;
        McpToolResult outcome;
        try
        {
            using var timeoutCts = McpTelemetryHelper.CreateTimeoutCts(options, cancellationToken);
            var resolve = await resolver.ResolveAsync(timeoutCts.Token);
            if (resolve.IsError)
            {
                outcome = McpTelemetryHelper.Error(resolve.ErrorMessage!);
            }
            else
            {
                conversationId = resolve.ConversationId;
                var take = McpTelemetryHelper.ResolveTake(options);
                outcome = await action(conversationId!.Value, take, timeoutCts.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            outcome = McpTelemetryHelper.Error("Telemetry query timed out.");
        }
        catch (Exception ex)
        {
            outcome = McpTelemetryHelper.Error($"Telemetry query failed: {ex.Message}");
        }

        auditLogger.Log(
            toolName,
            arguments: null,
            conversationId,
            outcome.RowCount,
            sw.ElapsedMilliseconds,
            ResolveCaller(),
            outcome.IsError);

        return outcome.Payload;
    }

    private string? ResolveCaller() =>
        httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
