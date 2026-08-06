using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services.CacheAlignment;
using Comprexy.Application.Services.Settings;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services.ChatTurn;

public sealed class OutgoingContextMaterializer
{
    private readonly ContextBuilder _contextBuilder;
    private readonly ICacheAlignmentService _cacheAlignment;
    private readonly IEffectiveSettingsAccessor _effectiveSettings;
    private readonly IOptionsMonitor<ContextPolicyOptions> _policy;
    private readonly IOptionsMonitor<CacheAlignmentOptions> _cacheAlignmentOptions;
    private readonly ILogger<OutgoingContextMaterializer> _logger;

    public OutgoingContextMaterializer(
        ContextBuilder contextBuilder,
        ICacheAlignmentService cacheAlignment,
        IEffectiveSettingsAccessor effectiveSettings,
        IOptionsMonitor<ContextPolicyOptions> policy,
        IOptionsMonitor<CacheAlignmentOptions> cacheAlignmentOptions,
        ILogger<OutgoingContextMaterializer> logger)
    {
        _contextBuilder = contextBuilder;
        _cacheAlignment = cacheAlignment;
        _effectiveSettings = effectiveSettings;
        _policy = policy;
        _cacheAlignmentOptions = cacheAlignmentOptions;
        _logger = logger;
    }

    /// <summary>Test / legacy ctor (internal so MS DI sees only the public primary).</summary>
    internal OutgoingContextMaterializer(
        ContextBuilder contextBuilder,
        ICacheAlignmentService cacheAlignment,
        IOptions<ContextPolicyOptions> policy,
        IOptions<CacheAlignmentOptions> cacheAlignmentOptions,
        ILogger<OutgoingContextMaterializer> logger)
        : this(
            contextBuilder,
            cacheAlignment,
            UnsetEffectiveSettingsAccessor.Instance,
            new FixedOptionsMonitor<ContextPolicyOptions>(policy),
            new FixedOptionsMonitor<CacheAlignmentOptions>(cacheAlignmentOptions),
            logger)
    {
    }


    public IReadOnlyList<ChatMessage> MaterializeOutgoingViaCacheAlignment(
        Conversation conversation,
        WorkingMemory? workingMemory,
        List<ConversationMessage> recentRaw,
        ChatMessage currentUserMessage,
        ConversationMessage currentMessageEntity,
        IReadOnlyList<ConversationMessage> allMessages,
        IReadOnlyList<ChatMessage>? pendingRuleMessages = null)
    {
        var messagesById = allMessages.ToDictionary(m => m.Id);
        var snapshot = _cacheAlignment.GetSnapshot(conversation.Id);
        var wmVersion = workingMemory?.Version ?? 0;

        if (snapshot is null
            || snapshot.WorkingMemoryVersion != wmVersion
            || snapshot.RetainFrontierWatermark > currentMessageEntity.Sequence)
        {
            // Cold ensure (or WM/watermark mismatch): rebuild wrap-up-ready Prefix from frontier.
            if (snapshot is not null)
            {
                _cacheAlignment.Invalidate(conversation.Id);
            }

            // Bake the failed-edit wire omit into the frozen Prefix instead of re-applying it every
            // turn during materialize: recentRaw excludes the tip, so the omit cannot drop the newest
            // message, and warm turns reuse stable Prefix bytes instead of rebuilding them.
            var frontierSource = ApplyLiveDuplicateFailedEditDedupe(
                conversation.Id,
                recentRaw,
                allMessages,
                currentMessageEntity.Sequence);

            if (!WrapUpReadiness.TryEnsureWrapUpReady(
                    frontierSource,
                    out var prefixFrontier,
                    out var excluded))
            {
                _logger.LogWarning(
                    "Cache Alignment EnsureWrapUpReady failed for conversation {ConversationId}; falling back to ContextBuilder.Build.",
                    conversation.Id);
                return _contextBuilder.Build(
                    conversation.SystemPrompt,
                    workingMemory,
                    frontierSource,
                    currentUserMessage,
                    pendingRuleMessages);
            }

            var prefix = _contextBuilder.BuildLivePrefix(
                conversation.SystemPrompt,
                workingMemory,
                prefixFrontier);
            var prefixIds = prefixFrontier.Select(m => m.Id).ToList();
            var watermark = prefixFrontier.Count == 0
                ? 0
                : prefixFrontier.Max(m => m.Sequence);

            if (!_cacheAlignment.TryStorePrefix(
                    conversation.Id,
                    prefix,
                    prefixIds,
                    wmVersion,
                    watermark,
                    catalogHash: null))
            {
                _logger.LogWarning(
                    "Cache Alignment TryStorePrefix rejected for conversation {ConversationId}; falling back to ContextBuilder.Build.",
                    conversation.Id);
                return _contextBuilder.Build(
                    conversation.SystemPrompt,
                    workingMemory,
                    frontierSource,
                    currentUserMessage,
                    pendingRuleMessages);
            }

            var suffixIds = excluded
                .Concat(new[] { currentMessageEntity })
                .Select(m => m.Id)
                .Distinct()
                .ToList();
            // Also include any unfolded messages after watermark not in Prefix (open tips).
            // Omitted duplicates above the watermark come back here by design: completeness wins
            // over savings outside the frozen Prefix.
            foreach (var message in allMessages.Where(m =>
                         !m.IsFolded &&
                         m.Sequence > watermark &&
                         m.Id != currentMessageEntity.Id))
            {
                if (!prefixIds.Contains(message.Id) && !suffixIds.Contains(message.Id))
                {
                    suffixIds.Add(message.Id);
                }
            }

            _cacheAlignment.ReplaceSuffix(conversation.Id, suffixIds);
        }
        else
        {
            // Warm: Suffix = unfolded after watermark (including tip); Prefix frozen.
            var suffixIds = allMessages
                .Where(m => !m.IsFolded && m.Sequence > snapshot.RetainFrontierWatermark)
                .OrderBy(m => m.Sequence)
                .Select(m => m.Id)
                .ToList();
            if (suffixIds.Count == 0 || suffixIds[^1] != currentMessageEntity.Id)
            {
                // Tip must be present even if sequence heuristic missed it.
                if (!suffixIds.Contains(currentMessageEntity.Id))
                {
                    suffixIds.Add(currentMessageEntity.Id);
                }
            }

            _cacheAlignment.ReplaceSuffix(conversation.Id, suffixIds);
        }

        // No materialize-time omit: Prefix ⊕ Suffix goes out verbatim so the tip is always present
        // and frozen Prefix bytes are never rewritten mid-conversation.
        return _cacheAlignment.MaterializeLive(conversation.Id, messagesById, pendingRuleMessages: pendingRuleMessages);
    }

    /// <summary>
    /// Repairs unfolded context so tool turns always follow an assistant/tool predecessor:
    /// restore a folded parent assistant when the live tip is a tool result, then drop any
    /// remaining orphan tools. Optionally omits older identical failed-edit tool turns from the
    /// wire (does not mark folded); Cache Alignment omits them at Prefix build instead. Logs when
    /// recovery or live dedupe runs so bad retain folds stay visible.
    /// </summary>
    public List<ConversationMessage> PrepareRecentRawForChatTemplate(
        Guid conversationId,
        List<ConversationMessage> recentRaw,
        ChatMessage tip,
        IReadOnlyList<ConversationMessage> allMessages,
        int tipSequence,
        bool applyLiveDedupe = true)
    {
        var (withParent, restored) = ChatTemplateMessageOrder.EnsureToolTipHasParent(
            recentRaw,
            tip,
            allMessages,
            tipSequence);
        if (restored > 0)
        {
            _logger.LogWarning(
                "Restored {RestoredCount} folded parent message(s) for tool tip in conversation {ConversationId} (chat template order).",
                restored,
                conversationId);
        }

        var (sanitized, dropped) = ChatTemplateMessageOrder.RemoveOrphanToolMessages(withParent);
        if (dropped > 0)
        {
            _logger.LogWarning(
                "Dropped {DroppedCount} orphan tool message(s) from outgoing context for conversation {ConversationId} (tool must follow assistant or tool).",
                dropped,
                conversationId);
        }

        var list = sanitized as List<ConversationMessage> ?? sanitized.ToList();
        if (!applyLiveDedupe)
        {
            return list;
        }

        return ApplyLiveDuplicateFailedEditDedupe(conversationId, list, allMessages, tipSequence);
    }

    /// <summary>
    /// Wire-only: drop older identical failed file-edit tool results (path + old_string
    /// last-wins) from the outgoing retain window so StrReplace failure loops do not stack.
    /// Does not <c>MarkFoldedInto</c>. The tip entity joins the corpus so a re-failing tip can
    /// displace older copies, then rows from the tip onward are stripped — callers own the tip
    /// (<see cref="ContextBuilder.Build"/> appends it; Cache Alignment carries it in the Suffix).
    /// </summary>
    public List<ConversationMessage> ApplyLiveDuplicateFailedEditDedupe(
        Guid conversationId,
        List<ConversationMessage> recentRaw,
        IReadOnlyList<ConversationMessage> allMessages,
        int tipSequence)
    {
        var dedupeEnabled = _effectiveSettings.IsSet
            ? _effectiveSettings.Current.DedupeDuplicateFailedEdits
            : _policy.CurrentValue.DedupeDuplicateFailedEdits;
        if (!dedupeEnabled || recentRaw.Count == 0)
        {
            return recentRaw;
        }

        var tipEntity = allMessages.FirstOrDefault(m => m.Sequence == tipSequence);
        IReadOnlyList<ConversationMessage> corpus = recentRaw;
        if (tipEntity is not null && recentRaw.TrueForAll(m => m.Sequence != tipSequence))
        {
            corpus = recentRaw.Append(tipEntity).OrderBy(m => m.Sequence).ToList();
        }

        var dedupe = DuplicateFailedEditDeduper.Apply(corpus, tipSequence);
        if (!dedupe.DroppedAny)
        {
            return recentRaw;
        }

        _logger.LogInformation(
            "duplicate_failed_edit_dedupe conversationId={ConversationId} phase=live_chat droppedCount={DroppedCount} keptKeys={KeptKeys} droppedSequences={DroppedSequences}",
            conversationId,
            dedupe.DroppedSequences.Count,
            string.Join(',', dedupe.KeptKeys),
            string.Join(',', dedupe.DroppedSequences));

        var keptPrior = dedupe.Retain
            .Where(m => m.Sequence < tipSequence)
            .OrderBy(m => m.Sequence)
            .ToList();

        var (sanitized, orphanDropped) = ChatTemplateMessageOrder.RemoveOrphanToolMessages(keptPrior);
        if (orphanDropped > 0)
        {
            _logger.LogWarning(
                "Dropped {DroppedCount} orphan tool message(s) after live duplicate-failed-edit dedupe for conversation {ConversationId}.",
                orphanDropped,
                conversationId);
        }

        return sanitized as List<ConversationMessage> ?? sanitized.ToList();
    }

    public List<ConversationMessage> SanitizeRecentRawForChatTemplate(
        Guid conversationId,
        List<ConversationMessage> recentRaw)
    {
        var (sanitized, dropped) = ChatTemplateMessageOrder.RemoveOrphanToolMessages(recentRaw);
        if (dropped > 0)
        {
            _logger.LogWarning(
                "Dropped {DroppedCount} orphan tool message(s) from outgoing context for conversation {ConversationId} (tool must follow assistant or tool).",
                dropped,
                conversationId);
        }

        return sanitized as List<ConversationMessage> ?? sanitized.ToList();
    }

    /// <summary>
    /// Every wire projection (retain omit, Prefix ⊕ Suffix materialize) must still end at the tip.
    /// A dropped tip hides the client's newest turn from the model — typically a mid-chain
    /// interrupt — so surface it and re-append instead of forwarding a truncated turn.
    /// </summary>
    public IReadOnlyList<ChatMessage> EnsureOutgoingEndsAtTip(
        Guid conversationId,
        IReadOnlyList<ChatMessage> outgoing,
        ChatMessage tip,
        int tipSequence)
    {
        var lastNonSystem = outgoing.LastOrDefault(m => m.Role != MessageRole.System);
        if (lastNonSystem is not null && IsSameChatMessage(lastNonSystem, tip))
        {
            return outgoing;
        }

        _logger.LogWarning(
            "Outgoing context for conversation {ConversationId} did not end at tip sequence {TipSequence}; re-appending the tip.",
            conversationId,
            tipSequence);

        var repaired = new List<ChatMessage>(outgoing.Count + 1);
        repaired.AddRange(outgoing);
        repaired.Add(tip);
        return repaired;
    }

    public static bool IsSameChatMessage(ChatMessage left, ChatMessage right)
    {
        if (left.Role != right.Role)
        {
            return false;
        }

        if (left.RawWireMessage is { } leftRaw && right.RawWireMessage is { } rightRaw)
        {
            return string.Equals(leftRaw.GetRawText(), rightRaw.GetRawText(), StringComparison.Ordinal);
        }

        return string.Equals(left.Content, right.Content, StringComparison.Ordinal);
    }
}
