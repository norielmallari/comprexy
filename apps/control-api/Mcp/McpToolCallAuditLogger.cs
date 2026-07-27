using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Comprexy.ControlApi.Mcp;

public sealed class McpToolCallAuditLogger(ILogger<McpToolCallAuditLogger> logger)
{
    private static readonly JsonSerializerOptions ArgumentHashJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void Log(
        string toolName,
        object? arguments,
        Guid? resolvedConversationId,
        int? rowCount,
        long durationMs,
        string? caller,
        bool isError,
        string? conversationSelector = null)
    {
        var argumentsHash = HashArguments(arguments);
        logger.LogInformation(
            "MCP tool call tool={ToolName} argumentsHash={ArgumentsHash} conversationId={ConversationId} conversationSelector={ConversationSelector} rowCount={RowCount} durationMs={DurationMs} caller={Caller} isError={IsError}",
            toolName,
            argumentsHash,
            resolvedConversationId,
            conversationSelector ?? resolvedConversationId?.ToString("D") ?? string.Empty,
            rowCount,
            durationMs,
            caller ?? string.Empty,
            isError);
    }

    public static string HashArguments(object? arguments)
    {
        if (arguments is null)
        {
            return "none";
        }

        var json = JsonSerializer.Serialize(arguments, ArgumentHashJsonOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash)[..16];
    }

    public static Stopwatch StartTimer() => Stopwatch.StartNew();
}
