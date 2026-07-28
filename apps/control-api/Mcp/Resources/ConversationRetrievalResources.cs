using System.ComponentModel;
using Comprexy.Application.Abstractions;
using Comprexy.ControlApi.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Comprexy.ControlApi.Mcp.Resources;

[McpServerResourceType]
public sealed class ConversationRetrievalResources(
    IConversationRetrievalQueryService retrievalQuery,
    IOptions<McpTelemetryOptions> options)
{
    [McpServerResource(UriTemplate = "comprexy://conversation/{conversationId}/working-memory", Name = "conversation_working_memory", MimeType = "application/json")]
    [Description("Latest working memory by conversation id.")]
    public Task<string> GetWorkingMemoryAsync(Guid conversationId, CancellationToken cancellationToken) =>
        ReadAsync(
            conversationId,
            async (id, _, ct) =>
            {
                if (!await retrievalQuery.ConversationExistsAsync(id, ct))
                {
                    return McpTelemetryHelper.ErrorJson($"Conversation not found: {id}");
                }

                var snapshot = await retrievalQuery.GetWorkingMemoryAsync(id, version: null, ct);
                return snapshot is null
                    ? McpTelemetryHelper.ErrorJson($"Working memory not found for conversation: {id}")
                    : McpTelemetryHelper.ToJson(snapshot);
            },
            cancellationToken);

    [McpServerResource(UriTemplate = "comprexy://conversation/{conversationId}/recent-messages", Name = "conversation_recent_messages", MimeType = "application/json")]
    [Description("Recent messages by conversation id.")]
    public Task<string> GetRecentMessagesAsync(Guid conversationId, CancellationToken cancellationToken) =>
        ReadAsync(
            conversationId,
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

    [McpServerResource(UriTemplate = "comprexy://conversation/{conversationId}/open-tool-chains", Name = "conversation_open_tool_chains", MimeType = "application/json")]
    [Description("Open tool-call chains by conversation id. isAwaitingClientToolResults marks tip-only in-flight batches.")]
    public Task<string> GetOpenToolChainsAsync(Guid conversationId, CancellationToken cancellationToken) =>
        ReadAsync(
            conversationId,
            async (id, _, ct) =>
            {
                var chains = await retrievalQuery.GetOpenToolChainsAsync(id, ct);
                return chains is null
                    ? McpTelemetryHelper.ErrorJson($"Conversation not found: {id}")
                    : McpTelemetryHelper.ToJson(chains);
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
            return McpTelemetryHelper.ErrorJson($"Retrieval query failed: {ex.Message}");
        }
    }
}
