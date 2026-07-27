using System.ComponentModel;
using Comprexy.Application.Abstractions;
using Comprexy.ControlApi.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Comprexy.ControlApi.Mcp.Tools;

[McpServerToolType]
public sealed class CurrentConversationRetrievalTools(
    IConversationRetrievalQueryService retrievalQuery,
    CurrentConversationResolver resolver,
    McpToolCallAuditLogger auditLogger,
    IOptions<McpTelemetryOptions> options,
    IHttpContextAccessor httpContextAccessor)
{
    [McpServerTool(Name = "search_current_conversation"), Description("Keyword search over the active conversation messages and working memory (requires X-Comprexy-Conversation-Id).")]
    public Task<string> SearchCurrentConversationAsync(
        [Description("Substring to match in message or working-memory content.")] string query,
        [Description("Max matches to return (clamped by McpTelemetry limits).")] int? limit = null,
        [Description("Include folded (compressed-away) messages. Default true.")] bool includeFolded = true,
        [Description("Include working-memory content matches. Default true.")] bool includeWorkingMemory = true,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "search_current_conversation",
            new { query, limit, includeFolded, includeWorkingMemory },
            async (conversationId, take, ct) =>
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

    [McpServerTool(Name = "get_current_message_window"), Description("Raw messages in a Sequence range for the active conversation (requires X-Comprexy-Conversation-Id). Uses Sequence, not TurnIndex.")]
    public Task<string> GetCurrentMessageWindowAsync(
        [Description("Inclusive start Sequence (>= 0).")] int sequenceStart,
        [Description("Inclusive end Sequence.")] int sequenceEnd,
        [Description("Include truncated RawWireJson. Default false.")] bool includeWireJson = false,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "get_current_message_window",
            new { sequenceStart, sequenceEnd, includeWireJson },
            async (conversationId, take, ct) =>
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

    [McpServerTool(Name = "get_current_recent_messages"), Description("Most recent messages for the active conversation (requires X-Comprexy-Conversation-Id).")]
    public Task<string> GetCurrentRecentMessagesAsync(
        [Description("When true, only unfolded (not yet folded) messages. Default false.")] bool unfoldedOnly = false,
        [Description("Include truncated RawWireJson. Default false.")] bool includeWireJson = false,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "get_current_recent_messages",
            new { unfoldedOnly, includeWireJson },
            async (conversationId, take, ct) =>
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

    [McpServerTool(Name = "get_current_working_memory"), Description("Working-memory snapshot for the active conversation (requires X-Comprexy-Conversation-Id). Omit version for latest.")]
    public Task<string> GetCurrentWorkingMemoryAsync(
        [Description("Optional working-memory version (>= 1). Omit for latest.")] int? version = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "get_current_working_memory",
            new { version },
            async (conversationId, _, ct) =>
            {
                try
                {
                    var snapshot = await retrievalQuery.GetWorkingMemoryAsync(conversationId, version, ct);
                    if (snapshot is null)
                    {
                        // Distinguish missing conversation vs missing WM: resolver already ensured existence.
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

    [McpServerTool(Name = "get_current_open_tool_chains"), Description("Open assistant tool_call ids in unfolded history for the active conversation (requires X-Comprexy-Conversation-Id). Same closed-chain rule as compression.")]
    public Task<string> GetCurrentOpenToolChainsAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "get_current_open_tool_chains",
            arguments: null,
            async (conversationId, _, ct) =>
            {
                var chains = await retrievalQuery.GetOpenToolChainsAsync(conversationId, ct);
                return chains is null
                    ? McpTelemetryHelper.Error($"Conversation not found: {conversationId}")
                    : McpTelemetryHelper.OkJson(chains, rowCount: chains.OpenToolCallIds.Count);
            },
            cancellationToken);

    private async Task<string> ExecuteAsync(
        string toolName,
        object? arguments,
        Func<Guid, int, CancellationToken, Task<McpToolResult>> action,
        CancellationToken cancellationToken = default)
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
            outcome = McpTelemetryHelper.Error($"Retrieval query failed: {ex.Message}");
        }

        auditLogger.Log(
            toolName,
            arguments,
            conversationId,
            outcome.RowCount,
            sw.ElapsedMilliseconds,
            httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            outcome.IsError);

        return outcome.Payload;
    }
}
