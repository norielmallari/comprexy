using System.Text.Json;
using System.Text.Json.Serialization;
using Comprexy.Application.Models.Telemetry;
using Comprexy.ControlApi.Configuration;
using Microsoft.Extensions.Options;

namespace Comprexy.ControlApi.Mcp;

internal static class McpTelemetryHelper
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static int ResolveTake(IOptions<McpTelemetryOptions> options, int? requestedTake = null)
    {
        var value = options.Value;
        return TelemetryQueryLimits.ClampTake(
            requestedTake,
            value.DefaultRowLimit,
            value.MaxRowLimit);
    }

    public static CancellationTokenSource CreateTimeoutCts(
        IOptions<McpTelemetryOptions> options,
        CancellationToken cancellationToken)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var seconds = Math.Max(1, options.Value.QueryTimeoutSeconds);
        linked.CancelAfter(TimeSpan.FromSeconds(seconds));
        return linked;
    }

    public static string ToJson<T>(T value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    public static McpToolResult OkJson<T>(T value, int rowCount) =>
        new()
        {
            Payload = ToJson(value),
            IsError = false,
            RowCount = rowCount
        };

    public static McpToolResult OkText(string payload, int rowCount = 1) =>
        new()
        {
            Payload = payload,
            IsError = false,
            RowCount = rowCount
        };

    public static McpToolResult Error(string message) =>
        new()
        {
            Payload = ToJson(new McpErrorPayload { IsError = true, Message = message }),
            IsError = true,
            RowCount = 0
        };

    public static McpToolResult NotFound(Guid conversationId) =>
        Error($"Conversation telemetry not found: {conversationId}");

    public static string ErrorJson(string message) => Error(message).Payload;

    public static string NotFoundJson(Guid conversationId) => NotFound(conversationId).Payload;
}

/// <summary>
/// Typed MCP tool outcome so audit logging does not parse serialized JSON.
/// </summary>
internal sealed class McpToolResult
{
    public required string Payload { get; init; }

    public bool IsError { get; init; }

    public int? RowCount { get; init; }
}

internal sealed class McpErrorPayload
{
    public bool IsError { get; init; }

    public string Message { get; init; } = string.Empty;
}
