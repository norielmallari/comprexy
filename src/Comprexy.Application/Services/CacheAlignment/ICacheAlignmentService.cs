using Comprexy.Application.Models;
using Comprexy.Domain.Entities;

namespace Comprexy.Application.Services.CacheAlignment;

/// <summary>
/// Immutable view of a conversation's Cache Alignment state.
/// </summary>
public sealed class CacheAlignmentSnapshot
{
    public CacheAlignmentSnapshot(
        Guid conversationId,
        IReadOnlyList<ChatMessage> prefix,
        IReadOnlyList<Guid> prefixMessageIds,
        IReadOnlyList<Guid> suffixMessageIds,
        int workingMemoryVersion,
        int retainFrontierWatermark,
        string? catalogHash)
    {
        ConversationId = conversationId;
        Prefix = prefix;
        PrefixMessageIds = prefixMessageIds;
        SuffixMessageIds = suffixMessageIds;
        WorkingMemoryVersion = workingMemoryVersion;
        RetainFrontierWatermark = retainFrontierWatermark;
        CatalogHash = catalogHash;
    }

    public Guid ConversationId { get; }

    /// <summary>Frozen wrap-up-ready model-facing messages (system + optional WM + closed raw).</summary>
    public IReadOnlyList<ChatMessage> Prefix { get; }

    /// <summary>ConversationMessage ids that contributed raw turns to <see cref="Prefix"/> (excludes synthetic system/WM).</summary>
    public IReadOnlyList<Guid> PrefixMessageIds { get; }

    /// <summary>Id-backed suffix (staged / open / tip turns not yet in Prefix).</summary>
    public IReadOnlyList<Guid> SuffixMessageIds { get; }

    public int WorkingMemoryVersion { get; }

    /// <summary>Max Sequence among Prefix raw messages (0 when Prefix has no raw turns).</summary>
    public int RetainFrontierWatermark { get; }

    /// <summary>Wire version key beside Prefix (tools catalog); not message bytes.</summary>
    public string? CatalogHash { get; }
}

public enum CacheAlignmentWrapUpMode
{
    StopTurn,
    MidChainPrefix
}

public sealed record CacheAlignmentWrapUpProjection(
    IReadOnlyList<ChatMessage> Messages,
    bool SoftFailed,
    string? SoftFailReason);

/// <summary>
/// Sole owner of the model-facing wrap-up-ready message Prefix for a conversation.
/// Call only while <see cref="IConversationRequestGate"/> exclusive lease is held.
/// </summary>
public interface ICacheAlignmentService
{
    CacheAlignmentSnapshot? GetSnapshot(Guid conversationId);

    /// <summary>
    /// Cold or replace Prefix from a wrap-up-ready ChatMessage list. Returns false on hard failure
    /// (previous Prefix left intact when present).
    /// </summary>
    bool TryStorePrefix(
        Guid conversationId,
        IReadOnlyList<ChatMessage> wrapUpReadyPrefix,
        IReadOnlyList<Guid> prefixMessageIds,
        int workingMemoryVersion,
        int retainFrontierWatermark,
        string? catalogHash);

    void AppendTip(Guid conversationId, Guid messageId);

    void ReplaceSuffix(Guid conversationId, IReadOnlyList<Guid> suffixMessageIds);

    void SetCatalogHash(Guid conversationId, string? catalogHash);

    void Invalidate(Guid conversationId);

    /// <summary>
    /// Prefix ⊕ resolved Suffix as ChatMessages. Optional ephemeral omit runs on a ConversationMessage
    /// corpus copy and must not mutate stored Prefix/Suffix. Optional pending rule messages splice
    /// after Prefix[0] (BaseSystem) and are never stored in Prefix bytes.
    /// </summary>
    IReadOnlyList<ChatMessage> MaterializeLive(
        Guid conversationId,
        IReadOnlyDictionary<Guid, ConversationMessage> messagesById,
        Func<IReadOnlyList<ConversationMessage>, IReadOnlyList<ConversationMessage>>? ephemeralOmit = null,
        IReadOnlyList<ChatMessage>? pendingRuleMessages = null);

    CacheAlignmentWrapUpProjection ProjectWrapUp(
        Guid conversationId,
        CacheAlignmentWrapUpMode mode,
        ChatMessage? visibleAssistant,
        ChatMessage wrapUpTip,
        IReadOnlyDictionary<Guid, ConversationMessage> messagesById,
        IReadOnlyList<ChatMessage>? liveMessages = null);

    /// <summary>
    /// Replace Prefix after WM accept; removes folded ids from Suffix. Fail-closed leaves prior Prefix.
    /// </summary>
    bool TryCommitWorkingMemory(
        Guid conversationId,
        IReadOnlyList<ChatMessage> wrapUpReadyPrefix,
        IReadOnlyList<Guid> prefixMessageIds,
        int workingMemoryVersion,
        int retainFrontierWatermark,
        IReadOnlySet<Guid> foldedMessageIds);
}
