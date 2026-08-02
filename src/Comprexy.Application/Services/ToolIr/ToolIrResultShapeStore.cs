using System.Collections.Concurrent;
using Comprexy.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services.ToolIr;

public readonly record struct ShapeSampleOutcome(
    bool ShouldEnqueue,
    IReadOnlyList<ToolIrShapeFeatures> Snapshot);

/// <summary>
/// Process-local store for probed/promoted result shapes and (when learner-enabled) sanitized samples.
/// </summary>
public sealed class ToolIrResultShapeStore
{
    private readonly ToolSchemaOptions _options;
    private readonly object _sync = new();
    private readonly Dictionary<Guid, ConversationShapes> _conversations = new();
    private readonly LinkedList<Guid> _lru = new();
    private readonly HashSet<Guid> _mirrorSuppressed = new();

    public ToolIrResultShapeStore(IOptions<ToolSchemaOptions> options)
    {
        _options = options.Value;
    }

    public bool TryGet(Guid conversationId, string clientTool, out ToolIrResultShape? shape)
    {
        shape = null;
        lock (_sync)
        {
            if (!_conversations.TryGetValue(conversationId, out var conv) ||
                !conv.Keys.TryGetValue(clientTool, out var entry) ||
                entry.Descriptor is null)
            {
                return false;
            }

            TouchLru(conversationId);
            shape = CloneShape(entry.Descriptor);
            return true;
        }
    }

    public void RecordProbe(Guid conversationId, string clientTool, ToolIrResultShape descriptor)
    {
        lock (_sync)
        {
            var conv = GetOrAddConversation(conversationId);
            if (!conv.Keys.TryGetValue(clientTool, out var entry))
            {
                entry = new KeyState();
                conv.Keys[clientTool] = entry;
            }

            if (entry.Descriptor is not null)
            {
                return;
            }

            entry.Descriptor = CloneShape(descriptor);
            entry.Descriptor.Source = ToolIrShapeSource.Probe;
            entry.Persisted = false;
            entry.State = KeyPhase.Probed;
            TouchLru(conversationId);
        }
    }

    public bool ShouldSample(Guid conversationId, string clientTool)
    {
        if (!_options.ResultShape.Learner.Enabled)
        {
            return false;
        }

        lock (_sync)
        {
            if (!_conversations.TryGetValue(conversationId, out var conv) ||
                !conv.Keys.TryGetValue(clientTool, out var entry))
            {
                return true;
            }

            if (entry.State == KeyPhase.Promoted)
            {
                return false;
            }

            return entry.AttemptCount < _options.ResultShape.MaxProposalAttemptsPerKey;
        }
    }

    public ShapeSampleOutcome RecordSample(
        Guid conversationId,
        string clientTool,
        ToolIrShapeFeatures features)
    {
        if (!_options.ResultShape.Learner.Enabled)
        {
            return new ShapeSampleOutcome(false, Array.Empty<ToolIrShapeFeatures>());
        }

        lock (_sync)
        {
            var conv = GetOrAddConversation(conversationId);
            if (!conv.Keys.TryGetValue(clientTool, out var entry))
            {
                entry = new KeyState();
                conv.Keys[clientTool] = entry;
            }

            if (entry.State == KeyPhase.Promoted ||
                entry.AttemptCount >= _options.ResultShape.MaxProposalAttemptsPerKey)
            {
                return new ShapeSampleOutcome(false, Array.Empty<ToolIrShapeFeatures>());
            }

            var max = Math.Max(1, _options.ResultShape.MaxSamplesRetained);
            if (features.ObservedBody is not null)
            {
                entry.AnchorRing.AddLast(features);
                while (entry.AnchorRing.Count > max)
                {
                    entry.AnchorRing.RemoveFirst();
                }
            }
            else
            {
                entry.AmbiguousRing.AddLast(features);
                while (entry.AmbiguousRing.Count > max)
                {
                    entry.AmbiguousRing.RemoveFirst();
                }
            }

            var total = entry.AnchorRing.Count + entry.AmbiguousRing.Count;
            var shouldEnqueue =
                entry.AnchorRing.Count > 0 &&
                entry.AmbiguousRing.Count > 0 &&
                total >= _options.ResultShape.MinSamplesBeforeProposal &&
                !entry.JobInFlight &&
                entry.AttemptCount < _options.ResultShape.MaxProposalAttemptsPerKey &&
                conv.PromotionCount < _options.ResultShape.Learner.MaxPromotionsPerConversation;

            IReadOnlyList<ToolIrShapeFeatures> snapshot = Array.Empty<ToolIrShapeFeatures>();
            if (shouldEnqueue)
            {
                entry.JobInFlight = true;
                entry.AttemptCount++;
                entry.State = KeyPhase.JobInFlight;
                var list = new List<ToolIrShapeFeatures>(total);
                list.AddRange(entry.AnchorRing);
                list.AddRange(entry.AmbiguousRing);
                snapshot = list;
            }

            TouchLru(conversationId);
            return new ShapeSampleOutcome(shouldEnqueue, snapshot);
        }
    }

    public void CompleteJob((Guid ConversationId, string ClientTool) key, bool promoted)
    {
        lock (_sync)
        {
            if (!_conversations.TryGetValue(key.ConversationId, out var conv) ||
                !conv.Keys.TryGetValue(key.ClientTool, out var entry))
            {
                return;
            }

            entry.JobInFlight = false;
            if (promoted)
            {
                entry.State = KeyPhase.Promoted;
                entry.AnchorRing.Clear();
                entry.AmbiguousRing.Clear();
            }
            else if (entry.AttemptCount >= _options.ResultShape.MaxProposalAttemptsPerKey)
            {
                entry.State = KeyPhase.Rejected;
                entry.AnchorRing.Clear();
                entry.AmbiguousRing.Clear();
            }
            else
            {
                entry.State = KeyPhase.Probed;
            }
        }
    }

    public void Promote((Guid ConversationId, string ClientTool) key, ToolIrResultShape descriptor)
    {
        lock (_sync)
        {
            var conv = GetOrAddConversation(key.ConversationId);
            if (!conv.Keys.TryGetValue(key.ClientTool, out var entry))
            {
                entry = new KeyState();
                conv.Keys[key.ClientTool] = entry;
            }

            if (conv.PromotionCount >= _options.ResultShape.Learner.MaxPromotionsPerConversation)
            {
                return;
            }

            entry.Descriptor = CloneShape(descriptor);
            entry.Descriptor.Source = ToolIrShapeSource.Learner;
            entry.Descriptor.ObservedAt = DateTimeOffset.UtcNow;
            entry.Persisted = false;
            entry.State = KeyPhase.Promoted;
            entry.AnchorRing.Clear();
            entry.AmbiguousRing.Clear();
            conv.PromotionCount++;
            TouchLru(key.ConversationId);
        }
    }

    public void Demote(Guid conversationId, string clientTool, string reason)
    {
        _ = reason;
        lock (_sync)
        {
            if (!_conversations.TryGetValue(conversationId, out var conv) ||
                !conv.Keys.TryGetValue(clientTool, out var entry))
            {
                return;
            }

            entry.Descriptor = null;
            entry.Persisted = false;
            entry.State = KeyPhase.Unknown;
            TouchLru(conversationId);
        }
    }

    public void Hydrate(Guid conversationId, Dictionary<string, ToolIrResultShape>? shapes)
    {
        lock (_sync)
        {
            _mirrorSuppressed.Remove(conversationId);
            var conv = GetOrAddConversation(conversationId);

            if (shapes is not null)
            {
                foreach (var (tool, shape) in shapes)
                {
                    if (conv.Keys.TryGetValue(tool, out var existing) && !existing.Persisted)
                    {
                        // Dirty wins — do not overwrite.
                        continue;
                    }

                    if (!conv.Keys.TryGetValue(tool, out var entry))
                    {
                        entry = new KeyState();
                        conv.Keys[tool] = entry;
                    }

                    entry.Descriptor = CloneShape(shape);
                    entry.Persisted = true;
                    entry.State = shape.Source == ToolIrShapeSource.Learner
                        ? KeyPhase.Promoted
                        : KeyPhase.Probed;
                }
            }

            // Drop clean keys absent from MappingJson.
            if (shapes is not null)
            {
                var toRemove = conv.Keys
                    .Where(kv => kv.Value.Persisted && kv.Value.Descriptor is not null && !shapes.ContainsKey(kv.Key))
                    .Select(kv => kv.Key)
                    .ToList();
                foreach (var key in toRemove)
                {
                    conv.Keys.Remove(key);
                }
            }

            TouchLru(conversationId);
        }
    }

    public IReadOnlyDictionary<string, ToolIrResultShape> PeekDirty(Guid conversationId)
    {
        lock (_sync)
        {
            if (_mirrorSuppressed.Contains(conversationId) ||
                !_conversations.TryGetValue(conversationId, out var conv))
            {
                return new Dictionary<string, ToolIrResultShape>();
            }

            var dirty = new Dictionary<string, ToolIrResultShape>(StringComparer.Ordinal);
            foreach (var (tool, entry) in conv.Keys)
            {
                if (!entry.Persisted && entry.Descriptor is not null)
                {
                    dirty[tool] = CloneShape(entry.Descriptor);
                }
            }

            return dirty;
        }
    }

    public bool IsMirrorSuppressed(Guid conversationId)
    {
        lock (_sync)
        {
            return _mirrorSuppressed.Contains(conversationId);
        }
    }

    public void MarkPersisted(Guid conversationId, IReadOnlyList<string> clientToolNames)
    {
        lock (_sync)
        {
            if (!_conversations.TryGetValue(conversationId, out var conv))
            {
                return;
            }

            foreach (var name in clientToolNames)
            {
                if (conv.Keys.TryGetValue(name, out var entry))
                {
                    entry.Persisted = true;
                }
            }
        }
    }

    public void SuppressMirror(Guid conversationId)
    {
        lock (_sync)
        {
            _mirrorSuppressed.Add(conversationId);
        }
    }

    private ConversationShapes GetOrAddConversation(Guid conversationId)
    {
        if (_conversations.TryGetValue(conversationId, out var existing))
        {
            return existing;
        }

        while (_conversations.Count >= Math.Max(1, _options.ResultShape.MaxConversations) && _lru.Count > 0)
        {
            var oldest = _lru.First!.Value;
            _lru.RemoveFirst();
            _conversations.Remove(oldest);
            _mirrorSuppressed.Remove(oldest);
        }

        var created = new ConversationShapes();
        _conversations[conversationId] = created;
        _lru.AddLast(conversationId);
        return created;
    }

    private void TouchLru(Guid conversationId)
    {
        var node = _lru.Find(conversationId);
        if (node is not null)
        {
            _lru.Remove(node);
            _lru.AddLast(node);
        }
        else
        {
            _lru.AddLast(conversationId);
        }
    }

    private static ToolIrResultShape CloneShape(ToolIrResultShape source) => new()
    {
        Envelope = source.Envelope,
        JsonField = source.JsonField,
        LinePrefix = source.LinePrefix,
        Source = source.Source,
        Samples = source.Samples,
        ObservedAt = source.ObservedAt
    };

    private enum KeyPhase
    {
        Unknown,
        Probed,
        JobInFlight,
        Promoted,
        Rejected
    }

    private sealed class ConversationShapes
    {
        public Dictionary<string, KeyState> Keys { get; } = new(StringComparer.Ordinal);
        public int PromotionCount { get; set; }
    }

    private sealed class KeyState
    {
        public ToolIrResultShape? Descriptor { get; set; }
        public bool Persisted { get; set; }
        public KeyPhase State { get; set; } = KeyPhase.Unknown;
        public int AttemptCount { get; set; }
        public bool JobInFlight { get; set; }
        public LinkedList<ToolIrShapeFeatures> AnchorRing { get; } = new();
        public LinkedList<ToolIrShapeFeatures> AmbiguousRing { get; } = new();
    }
}
