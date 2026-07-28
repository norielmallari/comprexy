using Comprexy.Application.Abstractions;

namespace Comprexy.ControlApi.Mcp;

/// <summary>
/// Resolves the current conversation from the MCP HTTP request header only.
/// Scoped per request; never caches identity across requests.
/// </summary>
public sealed class CurrentConversationResolver(
    IHttpContextAccessor httpContextAccessor,
    IConversationMetricsQueryService metricsQuery)
{
    public const string ConversationIdHeaderName = "X-Comprexy-Conversation-Id";

    public const string MissingHeaderMessage =
        "No current conversation header was supplied. Call the explicit conversation tool with conversationId set to the UUID from get_current_conversation_id (proxy ToolSchema meta-tool), or from operator tooling / X-Comprexy-Conversation-Id.";

    public async Task<CurrentConversationResolveResult> ResolveAsync(CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return CurrentConversationResolveResult.Error(MissingHeaderMessage);
        }

        if (!httpContext.Request.Headers.TryGetValue(ConversationIdHeaderName, out var values)
            || string.IsNullOrWhiteSpace(values.ToString()))
        {
            return CurrentConversationResolveResult.Error(MissingHeaderMessage);
        }

        var raw = values.ToString().Trim();
        if (!Guid.TryParse(raw, out var conversationId))
        {
            return CurrentConversationResolveResult.Error(
                $"Invalid {ConversationIdHeaderName} header value.");
        }

        if (!await metricsQuery.ConversationExistsAsync(conversationId, cancellationToken))
        {
            return CurrentConversationResolveResult.Error(
                $"Conversation not found: {conversationId}");
        }

        return CurrentConversationResolveResult.Ok(conversationId);
    }
}

public sealed class CurrentConversationResolveResult
{
    private CurrentConversationResolveResult(Guid? conversationId, string? errorMessage)
    {
        ConversationId = conversationId;
        ErrorMessage = errorMessage;
    }

    public Guid? ConversationId { get; }

    public string? ErrorMessage { get; }

    public bool IsError => ErrorMessage is not null;

    public static CurrentConversationResolveResult Ok(Guid conversationId) =>
        new(conversationId, null);

    public static CurrentConversationResolveResult Error(string message) =>
        new(null, message);
}
