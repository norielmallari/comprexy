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
    [McpServerTool(Name = "get_conversation_summary"), Description("Aggregate metrics for a specific conversation. Use when the client cannot forward X-Comprexy-Conversation-Id.")]
    public Task<string> GetConversationSummaryAsync(
        [Description("Conversation id from model context or operator tooling.")] Guid conversationId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            "get_conversation_summary",
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

    [McpServerTool(Name = "get_conversation_turns"), Description("Per-turn metrics for a specific conversation.")]
    public Task<string> GetConversationTurnsAsync(
        [Description("Conversation id from model context or operator tooling.")] Guid conversationId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            "get_conversation_turns",
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

    [McpServerTool(Name = "compare_conversations"), Description("Side-by-side comparison of two conversation telemetry summaries.")]
    public async Task<string> CompareConversationsAsync(
        [Description("Left conversation id.")] Guid leftConversationId,
        [Description("Right conversation id.")] Guid rightConversationId,
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
            "compare_conversations",
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
