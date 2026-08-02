using System.Collections.Concurrent;
using Comprexy.Application.Configuration;
using Comprexy.Application.Mapping;
using Comprexy.Application.Models;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services.CacheAlignment;

/// <summary>
/// Process-local Cache Alignment store. Prefix is immutable until Commit/Invalidate.
/// </summary>
public sealed class CacheAlignmentService : ICacheAlignmentService
{
    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
    private readonly object _evictLock = new();
    private readonly int _maxConversations;
    private long _accessClock;

    public CacheAlignmentService(IOptions<CacheAlignmentOptions> options)
    {
        _maxConversations = Math.Max(1, options.Value.MaxConversations);
    }

    public CacheAlignmentSnapshot? GetSnapshot(Guid conversationId)
    {
        if (!_entries.TryGetValue(conversationId, out var entry))
        {
            return null;
        }

        Touch(entry);
        return entry.ToSnapshot();
    }

    public bool TryStorePrefix(
        Guid conversationId,
        IReadOnlyList<ChatMessage> wrapUpReadyPrefix,
        IReadOnlyList<Guid> prefixMessageIds,
        int workingMemoryVersion,
        int retainFrontierWatermark,
        string? catalogHash)
    {
        if (!IsPrefixShapeValid(wrapUpReadyPrefix))
        {
            return false;
        }

        EnsureCapacity(conversationId);
        var entry = _entries.AddOrUpdate(
            conversationId,
            _ => new Entry(
                conversationId,
                Freeze(wrapUpReadyPrefix),
                FreezeIds(prefixMessageIds),
                Array.Empty<Guid>(),
                workingMemoryVersion,
                retainFrontierWatermark,
                catalogHash),
            (_, existing) =>
            {
                existing.ReplacePrefix(
                    Freeze(wrapUpReadyPrefix),
                    FreezeIds(prefixMessageIds),
                    workingMemoryVersion,
                    retainFrontierWatermark,
                    catalogHash ?? existing.CatalogHash);
                return existing;
            });
        Touch(entry);
        return true;
    }

    public void AppendTip(Guid conversationId, Guid messageId)
    {
        if (!_entries.TryGetValue(conversationId, out var entry))
        {
            return;
        }

        entry.AppendSuffixId(messageId);
        Touch(entry);
    }

    public void ReplaceSuffix(Guid conversationId, IReadOnlyList<Guid> suffixMessageIds)
    {
        if (!_entries.TryGetValue(conversationId, out var entry))
        {
            return;
        }

        entry.ReplaceSuffix(FreezeIds(suffixMessageIds));
        Touch(entry);
    }

    public void SetCatalogHash(Guid conversationId, string? catalogHash)
    {
        if (!_entries.TryGetValue(conversationId, out var entry))
        {
            return;
        }

        entry.SetCatalogHash(catalogHash);
        Touch(entry);
    }

    public void Invalidate(Guid conversationId) => _entries.TryRemove(conversationId, out _);

    public IReadOnlyList<ChatMessage> MaterializeLive(
        Guid conversationId,
        IReadOnlyDictionary<Guid, ConversationMessage> messagesById,
        Func<IReadOnlyList<ConversationMessage>, IReadOnlyList<ConversationMessage>>? ephemeralOmit = null)
    {
        if (!_entries.TryGetValue(conversationId, out var entry))
        {
            return Array.Empty<ChatMessage>();
        }

        Touch(entry);
        var prefix = entry.Prefix;
        var suffixIds = entry.SuffixMessageIds;
        var prefixIds = entry.PrefixMessageIds;

        if (ephemeralOmit is null)
        {
            return ConcatPrefixAndSuffix(prefix, suffixIds, messagesById);
        }

        var rawCorpus = new List<ConversationMessage>(prefixIds.Count + suffixIds.Count);
        foreach (var id in prefixIds.Concat(suffixIds))
        {
            if (messagesById.TryGetValue(id, out var message))
            {
                rawCorpus.Add(message);
            }
        }

        rawCorpus = rawCorpus.OrderBy(m => m.Sequence).ToList();
        var filtered = ephemeralOmit(rawCorpus).OrderBy(m => m.Sequence).ToList();

        // Preserve frozen Prefix bytes when omit did not drop any Prefix-resident row.
        var prefixIdSet = prefixIds.ToHashSet();
        var filteredIdSet = filtered.Select(m => m.Id).ToHashSet();
        var droppedPrefix = prefixIds.Any(id => !filteredIdSet.Contains(id));
        if (!droppedPrefix)
        {
            var keptSuffix = filtered.Where(m => !prefixIdSet.Contains(m.Id)).ToList();
            var preserved = new List<ChatMessage>(prefix.Count + keptSuffix.Count);
            preserved.AddRange(prefix);
            foreach (var message in keptSuffix)
            {
                preserved.Add(ConversationMessageMapper.ToChatMessage(message));
            }

            return preserved;
        }

        // Rebuild: system/WM heads + filtered raw (projection only; store untouched).
        var heads = TakeSystemHeads(prefix);
        var result = new List<ChatMessage>(heads.Count + filtered.Count);
        result.AddRange(heads);
        foreach (var message in filtered)
        {
            result.Add(ConversationMessageMapper.ToChatMessage(message));
        }

        return result;
    }

    public CacheAlignmentWrapUpProjection ProjectWrapUp(
        Guid conversationId,
        CacheAlignmentWrapUpMode mode,
        ChatMessage? visibleAssistant,
        ChatMessage wrapUpTip,
        IReadOnlyDictionary<Guid, ConversationMessage> messagesById,
        IReadOnlyList<ChatMessage>? liveMessages = null)
    {
        if (!_entries.TryGetValue(conversationId, out var entry))
        {
            return new CacheAlignmentWrapUpProjection(
                Array.Empty<ChatMessage>(),
                SoftFailed: true,
                SoftFailReason: "missing_snapshot");
        }

        Touch(entry);
        if (!IsPrefixShapeValid(entry.Prefix))
        {
            return new CacheAlignmentWrapUpProjection(
                Array.Empty<ChatMessage>(),
                SoftFailed: true,
                SoftFailReason: "prefix_not_ready");
        }

        var suffixMessages = ResolveSuffix(entry.SuffixMessageIds, messagesById);
        var (safeSuffix, _) = ChatTemplateMessageOrder.RemoveOrphanToolMessages(suffixMessages);

        var suffixAssessment = ToolCallChainState.Assess(safeSuffix);
        if (mode == CacheAlignmentWrapUpMode.StopTurn && suffixAssessment.IsOpen)
        {
            return new CacheAlignmentWrapUpProjection(
                Array.Empty<ChatMessage>(),
                SoftFailed: true,
                SoftFailReason: "suffix_open");
        }

        IReadOnlyList<ChatMessage> baseMessages;
        if (mode == CacheAlignmentWrapUpMode.MidChainPrefix && suffixAssessment.IsOpen)
        {
            if (!WrapUpReadiness.TryEnsureWrapUpReady(
                    safeSuffix,
                    out var closedSuffix,
                    out _))
            {
                return new CacheAlignmentWrapUpProjection(
                    Array.Empty<ChatMessage>(),
                    SoftFailed: true,
                    SoftFailReason: "suffix_open_unrepairable");
            }

            var rebuilt = new List<ChatMessage>(entry.Prefix.Count + closedSuffix.Count);
            rebuilt.AddRange(entry.Prefix);
            foreach (var message in closedSuffix.OrderBy(m => m.Sequence))
            {
                rebuilt.Add(ConversationMessageMapper.ToChatMessage(message));
            }

            baseMessages = rebuilt;
        }
        // Prefer live upstream bytes for closed live→wrap-up KV continuity (includes ephemeral omit).
        else if (liveMessages is { Count: > 0 })
        {
            baseMessages = liveMessages;
        }
        else
        {
            var rebuilt = new List<ChatMessage>(entry.Prefix.Count + safeSuffix.Count);
            rebuilt.AddRange(entry.Prefix);
            foreach (var message in safeSuffix.OrderBy(m => m.Sequence))
            {
                rebuilt.Add(ConversationMessageMapper.ToChatMessage(message));
            }

            baseMessages = rebuilt;
        }

        var messages = new List<ChatMessage>(baseMessages.Count + 2);
        messages.AddRange(baseMessages);

        if (mode == CacheAlignmentWrapUpMode.StopTurn && visibleAssistant is not null)
        {
            messages.Add(visibleAssistant);
        }

        messages.Add(wrapUpTip);
        return new CacheAlignmentWrapUpProjection(messages, SoftFailed: false, SoftFailReason: null);
    }

    public bool TryCommitWorkingMemory(
        Guid conversationId,
        IReadOnlyList<ChatMessage> wrapUpReadyPrefix,
        IReadOnlyList<Guid> prefixMessageIds,
        int workingMemoryVersion,
        int retainFrontierWatermark,
        IReadOnlySet<Guid> foldedMessageIds)
    {
        if (!_entries.TryGetValue(conversationId, out var entry))
        {
            // Cold commit after miss: store fresh Prefix.
            if (!TryStorePrefix(
                    conversationId,
                    wrapUpReadyPrefix,
                    prefixMessageIds,
                    workingMemoryVersion,
                    retainFrontierWatermark,
                    catalogHash: null))
            {
                return false;
            }

            if (_entries.TryGetValue(conversationId, out var created))
            {
                created.TrimSuffix(foldedMessageIds);
            }

            return true;
        }

        if (!IsPrefixShapeValid(wrapUpReadyPrefix))
        {
            return false;
        }

        entry.ReplacePrefix(
            Freeze(wrapUpReadyPrefix),
            FreezeIds(prefixMessageIds),
            workingMemoryVersion,
            retainFrontierWatermark,
            entry.CatalogHash);
        entry.TrimSuffix(foldedMessageIds);
        Touch(entry);
        return true;
    }

    /// <summary>R6 equality: ordered (Role, Content, RawWire text).</summary>
    public static bool ArePrefixEqual(IReadOnlyList<ChatMessage> a, IReadOnlyList<ChatMessage> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (a[i].Role != b[i].Role)
            {
                return false;
            }

            if (!string.Equals(a[i].Content, b[i].Content, StringComparison.Ordinal))
            {
                return false;
            }

            var aWire = a[i].RawWireMessage?.GetRawText();
            var bWire = b[i].RawWireMessage?.GetRawText();
            if (!string.Equals(aWire, bWire, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPrefixShapeValid(IReadOnlyList<ChatMessage> prefix)
    {
        if (prefix.Count == 0)
        {
            return false;
        }

        if (prefix[0].Role != MessageRole.System)
        {
            return false;
        }

        // Optional WM system only at index 1 — further system messages are allowed only as WM slot.
        return true;
    }

    private static IReadOnlyList<ChatMessage> TakeSystemHeads(IReadOnlyList<ChatMessage> prefix)
    {
        var heads = new List<ChatMessage>(2);
        if (prefix.Count == 0)
        {
            return heads;
        }

        heads.Add(prefix[0]);
        if (prefix.Count > 1 && prefix[1].Role == MessageRole.System)
        {
            heads.Add(prefix[1]);
        }

        return heads;
    }

    private static List<ChatMessage> ConcatPrefixAndSuffix(
        IReadOnlyList<ChatMessage> prefix,
        IReadOnlyList<Guid> suffixIds,
        IReadOnlyDictionary<Guid, ConversationMessage> messagesById)
    {
        var result = new List<ChatMessage>(prefix.Count + suffixIds.Count);
        result.AddRange(prefix);
        foreach (var message in ResolveSuffix(suffixIds, messagesById).OrderBy(m => m.Sequence))
        {
            result.Add(ConversationMessageMapper.ToChatMessage(message));
        }

        return result;
    }

    private static List<ConversationMessage> ResolveSuffix(
        IReadOnlyList<Guid> suffixIds,
        IReadOnlyDictionary<Guid, ConversationMessage> messagesById)
    {
        var list = new List<ConversationMessage>(suffixIds.Count);
        foreach (var id in suffixIds)
        {
            if (messagesById.TryGetValue(id, out var message))
            {
                list.Add(message);
            }
        }

        return list;
    }

    private static IReadOnlyList<ChatMessage> Freeze(IReadOnlyList<ChatMessage> messages) =>
        messages.ToArray();

    private static IReadOnlyList<Guid> FreezeIds(IReadOnlyList<Guid> ids) => ids.ToArray();

    private void Touch(Entry entry) => entry.LastAccessOrder = Interlocked.Increment(ref _accessClock);

    private void EnsureCapacity(Guid incomingConversationId)
    {
        if (_entries.ContainsKey(incomingConversationId) || _entries.Count < _maxConversations)
        {
            return;
        }

        lock (_evictLock)
        {
            if (_entries.ContainsKey(incomingConversationId) || _entries.Count < _maxConversations)
            {
                return;
            }

            var victim = _entries.Values.OrderBy(e => e.LastAccessOrder).FirstOrDefault();
            if (victim is not null)
            {
                _entries.TryRemove(victim.ConversationId, out _);
            }
        }
    }

    private sealed class Entry
    {
        private readonly object _gate = new();
        private IReadOnlyList<ChatMessage> _prefix;
        private IReadOnlyList<Guid> _prefixMessageIds;
        private IReadOnlyList<Guid> _suffixMessageIds;
        private int _workingMemoryVersion;
        private int _retainFrontierWatermark;
        private string? _catalogHash;

        public Entry(
            Guid conversationId,
            IReadOnlyList<ChatMessage> prefix,
            IReadOnlyList<Guid> prefixMessageIds,
            IReadOnlyList<Guid> suffixMessageIds,
            int workingMemoryVersion,
            int retainFrontierWatermark,
            string? catalogHash)
        {
            ConversationId = conversationId;
            _prefix = prefix;
            _prefixMessageIds = prefixMessageIds;
            _suffixMessageIds = suffixMessageIds;
            _workingMemoryVersion = workingMemoryVersion;
            _retainFrontierWatermark = retainFrontierWatermark;
            _catalogHash = catalogHash;
            LastAccessOrder = 0;
        }

        public Guid ConversationId { get; }

        public long LastAccessOrder { get; set; }

        public IReadOnlyList<ChatMessage> Prefix
        {
            get { lock (_gate) return _prefix; }
        }

        public IReadOnlyList<Guid> PrefixMessageIds
        {
            get { lock (_gate) return _prefixMessageIds; }
        }

        public IReadOnlyList<Guid> SuffixMessageIds
        {
            get { lock (_gate) return _suffixMessageIds; }
        }

        public string? CatalogHash
        {
            get { lock (_gate) return _catalogHash; }
        }

        public CacheAlignmentSnapshot ToSnapshot()
        {
            lock (_gate)
            {
                return new CacheAlignmentSnapshot(
                    ConversationId,
                    _prefix,
                    _prefixMessageIds,
                    _suffixMessageIds,
                    _workingMemoryVersion,
                    _retainFrontierWatermark,
                    _catalogHash);
            }
        }

        public void ReplacePrefix(
            IReadOnlyList<ChatMessage> prefix,
            IReadOnlyList<Guid> prefixMessageIds,
            int workingMemoryVersion,
            int retainFrontierWatermark,
            string? catalogHash)
        {
            lock (_gate)
            {
                _prefix = prefix;
                _prefixMessageIds = prefixMessageIds;
                _workingMemoryVersion = workingMemoryVersion;
                _retainFrontierWatermark = retainFrontierWatermark;
                _catalogHash = catalogHash;
            }
        }

        public void AppendSuffixId(Guid messageId)
        {
            lock (_gate)
            {
                if (_suffixMessageIds.Contains(messageId))
                {
                    return;
                }

                var next = new Guid[_suffixMessageIds.Count + 1];
                for (var i = 0; i < _suffixMessageIds.Count; i++)
                {
                    next[i] = _suffixMessageIds[i];
                }

                next[^1] = messageId;
                _suffixMessageIds = next;
            }
        }

        public void ReplaceSuffix(IReadOnlyList<Guid> suffixMessageIds)
        {
            lock (_gate)
            {
                _suffixMessageIds = suffixMessageIds;
            }
        }

        public void TrimSuffix(IReadOnlySet<Guid> foldedMessageIds)
        {
            lock (_gate)
            {
                if (foldedMessageIds.Count == 0 || _suffixMessageIds.Count == 0)
                {
                    return;
                }

                _suffixMessageIds = _suffixMessageIds.Where(id => !foldedMessageIds.Contains(id)).ToArray();
            }
        }

        public void SetCatalogHash(string? catalogHash)
        {
            lock (_gate)
            {
                _catalogHash = catalogHash;
            }
        }
    }
}
