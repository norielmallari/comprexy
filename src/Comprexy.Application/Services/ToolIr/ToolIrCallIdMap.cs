using System.Collections.Concurrent;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services.ToolIr;

public sealed record ToolIrCallMapping(
    Guid ConversationId,
    string IrCallId,
    string ClientCallId,
    string ComprexyToolName,
    string? ClientToolName,
    string IrArgumentsJson,
    string? ClientArgumentsJson,
    string Strategy,
    string? Path,
    int? StartLine,
    int? EndLine,
    bool Pending,
    DateTimeOffset RegisteredAt = default);

/// <summary>
/// Process-local hot cache for open Virtual Tools dual-id mappings.
/// SQLite (<c>IToolIrCallIdMapService</c>) is source of truth across process restart.
/// Evicts completed mappings; TTL-sweeps abandoned pending; bounds conversations and per-conversation pending.
/// </summary>
public sealed class ToolIrCallIdMap
{
    private const int MaxPendingPerConversation = 256;

    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, ToolIrCallMapping>> _byClientId = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, string>> _irToClient = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastActivityUtc = new();
    private readonly IClock _clock;
    private readonly TimeSpan _pendingTtl;
    private readonly int _maxConversations;

    public ToolIrCallIdMap(IClock clock, IOptions<ToolSchemaOptions> options)
    {
        _clock = clock;
        var opts = options.Value;
        _pendingTtl = opts.CallIdMapPendingAbsoluteExpiration <= TimeSpan.Zero
            ? TimeSpan.FromMinutes(30)
            : opts.CallIdMapPendingAbsoluteExpiration;
        _maxConversations = Math.Max(1, opts.CallIdMapMaxConversations);
    }

    public void Register(ToolIrCallMapping mapping)
    {
        SweepExpired();
        EnsureConversationCapacity(mapping.ConversationId);

        var stamped = mapping with
        {
            RegisteredAt = mapping.RegisteredAt == default ? _clock.UtcNow : mapping.RegisteredAt
        };

        var byClient = _byClientId.GetOrAdd(stamped.ConversationId, _ => new(StringComparer.Ordinal));
        var irMap = _irToClient.GetOrAdd(stamped.ConversationId, _ => new(StringComparer.Ordinal));

        if (byClient.Count >= MaxPendingPerConversation && !byClient.ContainsKey(stamped.ClientCallId))
        {
            throw new InvalidOperationException(
                $"Tool IR call-id map exceeded {MaxPendingPerConversation} pending entries for conversation {stamped.ConversationId}.");
        }

        byClient[stamped.ClientCallId] = stamped;
        irMap[stamped.IrCallId] = stamped.ClientCallId;
        Touch(stamped.ConversationId);
    }

    public bool TryGetByClientId(Guid conversationId, string clientCallId, out ToolIrCallMapping? mapping)
    {
        SweepExpired();
        mapping = null;
        if (!_byClientId.TryGetValue(conversationId, out var byClient))
        {
            return false;
        }

        if (!byClient.TryGetValue(clientCallId, out var found))
        {
            return false;
        }

        if (IsExpired(found))
        {
            Complete(conversationId, clientCallId);
            return false;
        }

        mapping = found;
        Touch(conversationId);
        return true;
    }

    public bool TryGetByIrId(Guid conversationId, string irCallId, out ToolIrCallMapping? mapping)
    {
        SweepExpired();
        mapping = null;
        if (!_irToClient.TryGetValue(conversationId, out var irMap) ||
            !irMap.TryGetValue(irCallId, out var clientId))
        {
            return false;
        }

        return TryGetByClientId(conversationId, clientId, out mapping);
    }

    public IReadOnlyCollection<string> GetPendingClientIds(Guid conversationId)
    {
        SweepExpired();
        if (!_byClientId.TryGetValue(conversationId, out var byClient))
        {
            return [];
        }

        return byClient.Where(kv => kv.Value.Pending && !IsExpired(kv.Value)).Select(kv => kv.Key).ToList();
    }

    public void Complete(Guid conversationId, string clientCallId)
    {
        if (!_byClientId.TryGetValue(conversationId, out var byClient))
        {
            return;
        }

        if (!byClient.TryRemove(clientCallId, out var mapping))
        {
            return;
        }

        if (_irToClient.TryGetValue(conversationId, out var irMap))
        {
            irMap.TryRemove(mapping.IrCallId, out _);
            if (irMap.IsEmpty)
            {
                _irToClient.TryRemove(conversationId, out _);
            }
        }

        if (byClient.IsEmpty)
        {
            _byClientId.TryRemove(conversationId, out _);
            _lastActivityUtc.TryRemove(conversationId, out _);
        }
        else
        {
            Touch(conversationId);
        }
    }

    /// <summary>
    /// Drops all dual-id entries for a conversation (final answers or abandoned local rounds).
    /// </summary>
    public void ClearConversation(Guid conversationId)
    {
        _byClientId.TryRemove(conversationId, out _);
        _irToClient.TryRemove(conversationId, out _);
        _lastActivityUtc.TryRemove(conversationId, out _);
    }

    /// <summary>
    /// When the persisted assistant has no open tool_calls, clear leftover pending for that conversation.
    /// Open IR→client rounds keep pending until inbound results arrive (or TTL).
    /// </summary>
    public void ClearIfNoOpenToolCalls(Guid conversationId, bool assistantHasOpenToolCalls)
    {
        if (!assistantHasOpenToolCalls)
        {
            ClearConversation(conversationId);
        }
        else
        {
            SweepExpired();
        }
    }

    public void SweepExpired()
    {
        var now = _clock.UtcNow;
        foreach (var (conversationId, byClient) in _byClientId.ToArray())
        {
            foreach (var (clientCallId, mapping) in byClient.ToArray())
            {
                if (IsExpired(mapping, now))
                {
                    Complete(conversationId, clientCallId);
                }
            }
        }
    }

    private bool IsExpired(ToolIrCallMapping mapping) => IsExpired(mapping, _clock.UtcNow);

    private bool IsExpired(ToolIrCallMapping mapping, DateTimeOffset now)
    {
        var registered = mapping.RegisteredAt == default ? now : mapping.RegisteredAt;
        return now - registered >= _pendingTtl;
    }

    private void Touch(Guid conversationId) =>
        _lastActivityUtc[conversationId] = _clock.UtcNow;

    private void EnsureConversationCapacity(Guid conversationId)
    {
        if (_byClientId.ContainsKey(conversationId))
        {
            return;
        }

        while (_byClientId.Count >= _maxConversations)
        {
            var victim = _lastActivityUtc
                .OrderBy(kv => kv.Value)
                .Select(kv => kv.Key)
                .FirstOrDefault(id => id != conversationId && _byClientId.ContainsKey(id));

            if (victim == Guid.Empty)
            {
                // Fall back: drop any other conversation key present in the map.
                victim = _byClientId.Keys.FirstOrDefault(id => id != conversationId);
            }

            if (victim == Guid.Empty)
            {
                break;
            }

            ClearConversation(victim);
        }
    }
}
