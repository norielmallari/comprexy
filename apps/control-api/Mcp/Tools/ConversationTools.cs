using System.ComponentModel;
using Comprexy.Application.Abstractions;
using Comprexy.ControlApi.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Comprexy.ControlApi.Mcp.Tools;

[McpServerToolType]
public sealed class ConversationTools(
    IConversationMetricsQueryService metricsQuery,
    McpToolCallAuditLogger auditLogger,
    IOptions<McpTelemetryOptions> options,
    IHttpContextAccessor httpContextAccessor)
{
    private const string ConversationIdDescription =
        "UUID from comprexy_get_current_conversation_id (proxy ToolSchema meta-tool), response header X-Comprexy-Conversation-Id, or operator tooling.";

    [McpServerTool(Name = "comprexy_get_conversation_summary"), Description("Aggregate metrics for a conversation.")]
    public Task<string> GetConversationSummaryAsync(
        [Description(ConversationIdDescription)] Guid conversationId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            "comprexy_get_conversation_summary",
            new { conversationId },
            conversationId.ToString("D"),
            async (take, ct) =>
            {
                if (!await metricsQuery.ConversationExistsAsync(conversationId, ct))
                {
                    return McpTelemetryHelper.Error($"Conversation not found: {conversationId}");
                }

                var summary = await metricsQuery.GetTelemetrySummaryAsync(conversationId, take, ct);
                return summary is null
                    ? McpTelemetryHelper.NotFound(conversationId)
                    : McpTelemetryHelper.OkJson(summary, rowCount: 1);
            },
            cancellationToken);

    [McpServerTool(Name = "comprexy_get_conversation_turns"), Description("Per-turn metrics for a conversation.")]
    public Task<string> GetConversationTurnsAsync(
        [Description(ConversationIdDescription)] Guid conversationId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            "comprexy_get_conversation_turns",
            new { conversationId },
            conversationId.ToString("D"),
            async (take, ct) =>
            {
                if (!await metricsQuery.ConversationExistsAsync(conversationId, ct))
                {
                    return McpTelemetryHelper.Error($"Conversation not found: {conversationId}");
                }

                var turns = await metricsQuery.GetTelemetryTurnsAsync(conversationId, take, ct);
                return McpTelemetryHelper.OkJson(turns, rowCount: turns.Count);
            },
            cancellationToken);

    [McpServerTool(Name = "comprexy_get_final_turn_snapshot"), Description("Final turn token proof for a conversation.")]
    public Task<string> GetFinalTurnSnapshotAsync(
        [Description(ConversationIdDescription)] Guid conversationId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            "comprexy_get_final_turn_snapshot",
            new { conversationId },
            conversationId.ToString("D"),
            async (_, ct) =>
            {
                if (!await metricsQuery.ConversationExistsAsync(conversationId, ct))
                {
                    return McpTelemetryHelper.Error($"Conversation not found: {conversationId}");
                }

                var snapshot = await metricsQuery.GetFinalTurnSnapshotAsync(conversationId, ct);
                return snapshot is null
                    ? McpTelemetryHelper.NotFound(conversationId)
                    : McpTelemetryHelper.OkJson(snapshot, rowCount: 1);
            },
            cancellationToken);

    [McpServerTool(Name = "comprexy_get_compression_phase_breakdown"), Description("Compression phase breakdown for a conversation.")]
    public Task<string> GetCompressionPhaseBreakdownAsync(
        [Description(ConversationIdDescription)] Guid conversationId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            "comprexy_get_compression_phase_breakdown",
            new { conversationId },
            conversationId.ToString("D"),
            async (take, ct) =>
            {
                if (!await metricsQuery.ConversationExistsAsync(conversationId, ct))
                {
                    return McpTelemetryHelper.Error($"Conversation not found: {conversationId}");
                }

                var phases = await metricsQuery.GetPhaseBreakdownAsync(conversationId, take, ct);
                return McpTelemetryHelper.OkJson(phases, rowCount: phases.Count);
            },
            cancellationToken);

    [McpServerTool(Name = "comprexy_get_budget_events"), Description("Budget and trim events for a conversation.")]
    public Task<string> GetBudgetEventsAsync(
        [Description(ConversationIdDescription)] Guid conversationId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            "comprexy_get_budget_events",
            new { conversationId },
            conversationId.ToString("D"),
            async (take, ct) =>
            {
                if (!await metricsQuery.ConversationExistsAsync(conversationId, ct))
                {
                    return McpTelemetryHelper.Error($"Conversation not found: {conversationId}");
                }

                var events = await metricsQuery.GetBudgetEventsAsync(conversationId, take, ct);
                return events is null
                    ? McpTelemetryHelper.NotFound(conversationId)
                    : McpTelemetryHelper.OkJson(events, rowCount: 1);
            },
            cancellationToken);

    [McpServerTool(Name = "comprexy_get_evidence_markdown"), Description("Commit-ready evidence markdown for a conversation.")]
    public Task<string> GetEvidenceMarkdownAsync(
        [Description(ConversationIdDescription)] Guid conversationId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            "comprexy_get_evidence_markdown",
            new { conversationId },
            conversationId.ToString("D"),
            async (take, ct) =>
            {
                if (!await metricsQuery.ConversationExistsAsync(conversationId, ct))
                {
                    return McpTelemetryHelper.Error($"Conversation not found: {conversationId}");
                }

                var markdown = await metricsQuery.GetEvidenceMarkdownAsync(conversationId, take, ct);
                return markdown is null
                    ? McpTelemetryHelper.NotFound(conversationId)
                    : McpTelemetryHelper.OkText(markdown, rowCount: 1);
            },
            cancellationToken);

    [McpServerTool(Name = "comprexy_get_prompt_growth_timeline"), Description("Actual prompt tokens per turn for a conversation.")]
    public Task<string> GetPromptGrowthTimelineAsync(
        [Description(ConversationIdDescription)] Guid conversationId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            "comprexy_get_prompt_growth_timeline",
            new { conversationId },
            conversationId.ToString("D"),
            async (take, ct) =>
            {
                if (!await metricsQuery.ConversationExistsAsync(conversationId, ct))
                {
                    return McpTelemetryHelper.Error($"Conversation not found: {conversationId}");
                }

                var timeline = await metricsQuery.GetPromptGrowthTimelineAsync(conversationId, take, ct);
                return timeline is null
                    ? McpTelemetryHelper.NotFound(conversationId)
                    : McpTelemetryHelper.OkJson(timeline, rowCount: timeline.Points.Count);
            },
            cancellationToken);

    [McpServerTool(Name = "comprexy_compare_conversations"), Description("Side-by-side comparison of two conversation telemetry summaries.")]
    public async Task<string> CompareConversationsAsync(
        [Description("Left ConversationId (UUID from comprexy_get_current_conversation_id or operator tooling).")] Guid leftConversationId,
        [Description("Right ConversationId (UUID from comprexy_get_current_conversation_id or operator tooling).")] Guid rightConversationId,
        CancellationToken cancellationToken)
    {
        var sw = McpToolCallAuditLogger.StartTimer();
        var selector = $"{leftConversationId:D},{rightConversationId:D}";
        McpToolResult outcome;
        try
        {
            using var timeoutCts = McpTelemetryHelper.CreateTimeoutCts(options, cancellationToken);
            var take = McpTelemetryHelper.ResolveTake(options);
            if (!await metricsQuery.ConversationExistsAsync(leftConversationId, timeoutCts.Token)
                || !await metricsQuery.ConversationExistsAsync(rightConversationId, timeoutCts.Token))
            {
                outcome = McpTelemetryHelper.Error("One or both conversations were not found.");
            }
            else
            {
                var comparison = await metricsQuery.CompareConversationsAsync(
                    leftConversationId,
                    rightConversationId,
                    take,
                    timeoutCts.Token);
                outcome = comparison is null
                    ? McpTelemetryHelper.Error(
                        "Telemetry summary missing for one or both conversations.")
                    : McpTelemetryHelper.OkJson(comparison, rowCount: 2);
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
            "comprexy_compare_conversations",
            new { leftConversationId, rightConversationId },
            resolvedConversationId: null,
            outcome.RowCount,
            sw.ElapsedMilliseconds,
            httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            outcome.IsError,
            conversationSelector: selector);

        return outcome.Payload;
    }

    private async Task<string> ExecuteAsync(
        string toolName,
        object arguments,
        string conversationSelector,
        Func<int, CancellationToken, Task<McpToolResult>> action,
        CancellationToken cancellationToken)
    {
        var sw = McpToolCallAuditLogger.StartTimer();
        Guid? conversationId = Guid.TryParse(conversationSelector, out var parsed) ? parsed : null;
        McpToolResult outcome;
        try
        {
            using var timeoutCts = McpTelemetryHelper.CreateTimeoutCts(options, cancellationToken);
            var take = McpTelemetryHelper.ResolveTake(options);
            outcome = await action(take, timeoutCts.Token);
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
            arguments,
            conversationId,
            outcome.RowCount,
            sw.ElapsedMilliseconds,
            httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            outcome.IsError,
            conversationSelector: conversationSelector);

        return outcome.Payload;
    }
}
