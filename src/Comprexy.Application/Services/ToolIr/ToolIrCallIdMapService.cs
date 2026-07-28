using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Domain.Entities;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services.ToolIr;

/// <summary>
/// Write-through dual-id map service. Commits via an isolated map UoW before updating the hot cache
/// on register so open IR→client rounds survive process restart without flushing the chat unit.
/// </summary>
public sealed class ToolIrCallIdMapService : IToolIrCallIdMapService
{
    private readonly ToolIrCallIdMap _hotCache;
    private readonly IToolIrCallIdMapUnitOfWorkFactory _mapUowFactory;
    private readonly IClock _clock;
    private readonly TimeSpan _pendingTtl;

    public ToolIrCallIdMapService(
        ToolIrCallIdMap hotCache,
        IToolIrCallIdMapUnitOfWorkFactory mapUowFactory,
        IClock clock,
        IOptions<ToolSchemaOptions> options)
    {
        _hotCache = hotCache;
        _mapUowFactory = mapUowFactory;
        _clock = clock;
        var opts = options.Value;
        _pendingTtl = opts.CallIdMapPendingAbsoluteExpiration <= TimeSpan.Zero
            ? TimeSpan.FromMinutes(30)
            : opts.CallIdMapPendingAbsoluteExpiration;
    }

    public async Task RegisterAsync(ToolIrCallMapping mapping, CancellationToken cancellationToken)
    {
        await SweepExpiredAsync(cancellationToken);

        var registeredAt = mapping.RegisteredAt == default ? _clock.UtcNow : mapping.RegisteredAt;
        var stamped = mapping with { RegisteredAt = registeredAt, Pending = true };

        await using (var mapUow = _mapUowFactory.Create())
        {
            mapUow.Maps.Add(ConversationToolCallMap.CreatePending(
                stamped.ConversationId,
                stamped.IrCallId,
                stamped.ClientCallId,
                stamped.ComprexyToolName,
                stamped.ClientToolName,
                stamped.IrArgumentsJson,
                stamped.ClientArgumentsJson,
                stamped.Strategy,
                stamped.Path,
                stamped.StartLine,
                stamped.EndLine,
                stamped.RegisteredAt));

            // Durability: isolated commit before client-facing tool_calls leave the proxy.
            await mapUow.SaveChangesAsync(cancellationToken);
        }

        _hotCache.Register(stamped);
    }

    public async Task<ToolIrCallMapping?> TryGetByClientIdAsync(
        Guid conversationId,
        string clientCallId,
        CancellationToken cancellationToken)
    {
        await SweepExpiredAsync(cancellationToken);

        if (_hotCache.TryGetByClientId(conversationId, clientCallId, out var cached) && cached is not null)
        {
            return cached;
        }

        await using (var mapUow = _mapUowFactory.Create())
        {
            var row = await mapUow.Maps.FindPendingByClientCallIdAsync(
                conversationId,
                clientCallId,
                cancellationToken);
            if (row is null)
            {
                return null;
            }

            if (IsExpired(row.RegisteredAt))
            {
                await mapUow.Maps.DeleteByClientCallIdAsync(conversationId, clientCallId, cancellationToken);
                await mapUow.SaveChangesAsync(cancellationToken);
                _hotCache.Complete(conversationId, clientCallId);
                return null;
            }

            var mapping = ToMapping(row);
            _hotCache.Register(mapping);
            return mapping;
        }
    }

    public async Task CompleteAsync(Guid conversationId, string clientCallId, CancellationToken cancellationToken)
    {
        await using (var mapUow = _mapUowFactory.Create())
        {
            await mapUow.Maps.DeleteByClientCallIdAsync(conversationId, clientCallId, cancellationToken);
            await mapUow.SaveChangesAsync(cancellationToken);
        }

        _hotCache.Complete(conversationId, clientCallId);
    }

    public async Task ClearIfNoOpenToolCallsAsync(
        Guid conversationId,
        bool assistantHasOpenToolCalls,
        CancellationToken cancellationToken)
    {
        if (!assistantHasOpenToolCalls)
        {
            await using (var mapUow = _mapUowFactory.Create())
            {
                await mapUow.Maps.DeletePendingByConversationIdAsync(conversationId, cancellationToken);
                await mapUow.SaveChangesAsync(cancellationToken);
            }

            _hotCache.ClearConversation(conversationId);
            return;
        }

        await SweepExpiredAsync(cancellationToken);
    }

    private async Task SweepExpiredAsync(CancellationToken cancellationToken)
    {
        _hotCache.SweepExpired();
        var cutoff = _clock.UtcNow - _pendingTtl;
        await using (var mapUow = _mapUowFactory.Create())
        {
            var deleted = await mapUow.Maps.DeleteExpiredPendingAsync(cutoff, cancellationToken);
            if (deleted > 0)
            {
                await mapUow.SaveChangesAsync(cancellationToken);
            }
        }
    }

    private bool IsExpired(DateTimeOffset registeredAt) =>
        _clock.UtcNow - registeredAt >= _pendingTtl;

    private static ToolIrCallMapping ToMapping(ConversationToolCallMap row) =>
        new(
            row.ConversationId,
            row.IrCallId,
            row.ClientCallId,
            row.ComprexyToolName,
            row.ClientToolName,
            row.IrArgumentsJson,
            row.ClientArgumentsJson,
            row.Strategy,
            row.Path,
            row.StartLine,
            row.EndLine,
            row.Pending,
            row.RegisteredAt);
}
