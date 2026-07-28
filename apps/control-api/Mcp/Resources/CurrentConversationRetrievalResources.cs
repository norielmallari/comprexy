using System.ComponentModel;
using Comprexy.Application.Abstractions;
using Comprexy.ControlApi.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Comprexy.ControlApi.Mcp.Resources;

[McpServerResourceType]
public sealed class CurrentConversationRetrievalResources(
    IConversationRetrievalQueryService retrievalQuery,
    CurrentConversationResolver resolver,
    IOptions<McpTelemetryOptions> options)
{
    [McpServerResource(UriTemplate = "comprexy://current/working-memory", Name = "current_working_memory", MimeType = "application/json")]
    [Description("Current conversation latest working memory (requires X-Comprexy-Conversation-Id).")]
    public Task<string> GetWorkingMemoryAsync(CancellationToken cancellationToken) =>
        ReadAsync(
            async (id, _, ct) =>
            {
                var snapshot = await retrievalQuery.GetWorkingMemoryAsync(id, version: null, ct);
                return snapshot is null
                    ? McpTelemetryHelper.ErrorJson($"Working memory not found for conversation: {id}")
                    : McpTelemetryHelper.ToJson(snapshot);
            },
            cancellationToken);

    [McpServerResource(UriTemplate = "comprexy://current/recent-messages", Name = "current_recent_messages", MimeType = "application/json")]
    [Description("Current conversation recent messages (requires X-Comprexy-Conversation-Id).")]
    public Task<string> GetRecentMessagesAsync(CancellationToken cancellationToken) =>
        ReadAsync(
            async (id, take, ct) =>
            {
                var messages = await retrievalQuery.GetRecentMessagesAsync(
                    id,
                    take,
                    unfoldedOnly: false,
                    includeWireJson: false,
                    ct);
                return messages is null
                    ? McpTelemetryHelper.ErrorJson($"Conversation not found: {id}")
                    : McpTelemetryHelper.ToJson(messages);
            },
            cancellationToken);

    [McpServerResource(UriTemplate = "comprexy://current/open-tool-chains", Name = "current_open_tool_chains", MimeType = "application/json")]
    [Description("Current conversation open tool-call chains (requires X-Comprexy-Conversation-Id).")]
    public Task<string> GetOpenToolChainsAsync(CancellationToken cancellationToken) =>
        ReadAsync(
            async (id, _, ct) =>
            {
                var chains = await retrievalQuery.GetOpenToolChainsAsync(id, ct);
                return chains is null
                    ? McpTelemetryHelper.ErrorJson($"Conversation not found: {id}")
                    : McpTelemetryHelper.ToJson(chains);
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
