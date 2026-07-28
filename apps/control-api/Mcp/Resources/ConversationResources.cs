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
    [Description("Explicit conversation summary by id.")]
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
    [Description("Explicit conversation per-turn metrics by id.")]
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
