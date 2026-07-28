using System.ComponentModel;
using Comprexy.Application.Abstractions;
using Comprexy.ControlApi.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Comprexy.ControlApi.Mcp.Tools;

[McpServerToolType]
public sealed class ConversationRetrievalTools(
    IConversationRetrievalQueryService retrievalQuery,
    McpToolCallAuditLogger auditLogger,
    IOptions<McpTelemetryOptions> options,
    IHttpContextAccessor httpContextAccessor)
{
    [McpServerTool(Name = "search_conversation"), Description("Keyword search over a conversation's messages and working memory. Use when the client cannot forward X-Comprexy-Conversation-Id.")]
    public Task<string> SearchConversationAsync(
        [Description("UUID from get_current_conversation_id (proxy ToolSchema meta-tool), X-Comprexy-Conversation-Id, or operator tooling.")] Guid conversationId,
        [Description("Substring to match in message or working-memory content.")] string query,
        [Description("Max matches to return (clamped by McpTelemetry limits).")] int? limit = null,
        [Description("Include folded (compressed-away) messages. Default true.")] bool includeFolded = true,
        [Description("Include working-memory content matches. Default true.")] bool includeWorkingMemory = true,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "search_conversation",
            new { conversationId, query, limit, includeFolded, includeWorkingMemory },
            conversationId,
            async (take, ct) =>
            {
                try
                {
                    var result = await retrievalQuery.SearchAsync(
                        conversationId,
                        query,
                        limit ?? take,
                        includeFolded,
                        includeWorkingMemory,
                        ct);
                    return result is null
                        ? McpTelemetryHelper.Error($"Conversation not found: {conversationId}")
                        : McpTelemetryHelper.OkJson(result, rowCount: result.Matches.Count);
                }
                catch (ArgumentException ex)
                {
                    return McpTelemetryHelper.Error(ex.Message);
                }
            },
            cancellationToken);

    [McpServerTool(Name = "get_message_window"), Description("Raw messages in a Sequence range. Uses Sequence, not TurnIndex.")]
    public Task<string> GetMessageWindowAsync(
        [Description("UUID from get_current_conversation_id (proxy ToolSchema meta-tool), X-Comprexy-Conversation-Id, or operator tooling.")] Guid conversationId,
        [Description("Inclusive start Sequence (>= 0).")] int sequenceStart,
        [Description("Inclusive end Sequence.")] int sequenceEnd,
        [Description("Include truncated RawWireJson. Default false.")] bool includeWireJson = false,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "get_message_window",
            new { conversationId, sequenceStart, sequenceEnd, includeWireJson },
            conversationId,
            async (take, ct) =>
            {
                try
                {
                    var messages = await retrievalQuery.GetMessageWindowAsync(
                        conversationId,
                        sequenceStart,
                        sequenceEnd,
                        includeWireJson,
                        take,
                        ct);
                    return messages is null
                        ? McpTelemetryHelper.Error($"Conversation not found: {conversationId}")
                        : McpTelemetryHelper.OkJson(messages, rowCount: messages.Count);
                }
                catch (ArgumentException ex)
                {
                    return McpTelemetryHelper.Error(ex.Message);
                }
            },
            cancellationToken);

    [McpServerTool(Name = "get_recent_messages"), Description("Most recent messages for a conversation.")]
    public Task<string> GetRecentMessagesAsync(
        [Description("UUID from get_current_conversation_id (proxy ToolSchema meta-tool), X-Comprexy-Conversation-Id, or operator tooling.")] Guid conversationId,
        [Description("When true, only unfolded (not yet folded) messages. Default false.")] bool unfoldedOnly = false,
        [Description("Include truncated RawWireJson. Default false.")] bool includeWireJson = false,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "get_recent_messages",
            new { conversationId, unfoldedOnly, includeWireJson },
            conversationId,
            async (take, ct) =>
            {
                var messages = await retrievalQuery.GetRecentMessagesAsync(
                    conversationId,
                    take,
                    unfoldedOnly,
                    includeWireJson,
                    ct);
                return messages is null
                    ? McpTelemetryHelper.Error($"Conversation not found: {conversationId}")
                    : McpTelemetryHelper.OkJson(messages, rowCount: messages.Count);
            },
            cancellationToken);

    [McpServerTool(Name = "get_working_memory"), Description("Working-memory snapshot. Omit version for latest.")]
    public Task<string> GetWorkingMemoryAsync(
        [Description("UUID from get_current_conversation_id (proxy ToolSchema meta-tool), X-Comprexy-Conversation-Id, or operator tooling.")] Guid conversationId,
        [Description("Optional working-memory version (>= 1). Omit for latest.")] int? version = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "get_working_memory",
            new { conversationId, version },
            conversationId,
            async (_, ct) =>
            {
                try
                {
                    if (!await retrievalQuery.ConversationExistsAsync(conversationId, ct))
                    {
                        return McpTelemetryHelper.Error($"Conversation not found: {conversationId}");
                    }

                    var snapshot = await retrievalQuery.GetWorkingMemoryAsync(conversationId, version, ct);
                    if (snapshot is null)
                    {
                        return version is null
                            ? McpTelemetryHelper.Error($"Working memory not found for conversation: {conversationId}")
                            : McpTelemetryHelper.Error(
                                $"Working memory version {version} not found for conversation: {conversationId}");
                    }

                    return McpTelemetryHelper.OkJson(snapshot, rowCount: 1);
                }
                catch (ArgumentException ex)
                {
                    return McpTelemetryHelper.Error(ex.Message);
                }
            },
            cancellationToken);

    [McpServerTool(Name = "get_open_tool_chains"), Description("Open assistant tool_call ids in unfolded history. Same closed-chain rule as compression.")]
    public Task<string> GetOpenToolChainsAsync(
        [Description("UUID from get_current_conversation_id (proxy ToolSchema meta-tool), X-Comprexy-Conversation-Id, or operator tooling.")] Guid conversationId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "get_open_tool_chains",
            new { conversationId },
            conversationId,
            async (_, ct) =>
            {
                var chains = await retrievalQuery.GetOpenToolChainsAsync(conversationId, ct);
                return chains is null
                    ? McpTelemetryHelper.Error($"Conversation not found: {conversationId}")
                    : McpTelemetryHelper.OkJson(chains, rowCount: chains.OpenToolCallIds.Count);
            },
            cancellationToken);

    private async Task<string> ExecuteAsync(
        string toolName,
        object arguments,
        Guid conversationId,
        Func<int, CancellationToken, Task<McpToolResult>> action,
        CancellationToken cancellationToken = default)
    {
        var sw = McpToolCallAuditLogger.StartTimer();
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
            outcome = McpTelemetryHelper.Error($"Retrieval query failed: {ex.Message}");
        }

        auditLogger.Log(
            toolName,
            arguments,
            conversationId,
            outcome.RowCount,
            sw.ElapsedMilliseconds,
            httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            outcome.IsError,
            conversationSelector: conversationId.ToString("D"));

        return outcome.Payload;
    }
}
