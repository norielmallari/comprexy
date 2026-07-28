using Comprexy.Application.Services.ToolIr;

namespace Comprexy.Application.Abstractions;

/// <summary>
/// Write-through dual-id map: SQLite source of truth + in-memory hot cache.
/// </summary>
public interface IToolIrCallIdMapService
{
    /// <summary>
    /// Persists the mapping (committed) then updates the hot cache.
    /// Must complete before client-facing tool_calls leave the proxy.
    /// </summary>
    Task RegisterAsync(ToolIrCallMapping mapping, CancellationToken cancellationToken);

    Task<ToolIrCallMapping?> TryGetByClientIdAsync(
        Guid conversationId,
        string clientCallId,
        CancellationToken cancellationToken);

    Task CompleteAsync(Guid conversationId, string clientCallId, CancellationToken cancellationToken);

    Task ClearIfNoOpenToolCallsAsync(
        Guid conversationId,
        bool assistantHasOpenToolCalls,
        CancellationToken cancellationToken);
}
