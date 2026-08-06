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

public sealed class ClientHistorySynchronizer
{
    private readonly IConversationMessageRepository _messageRepository;
    private readonly IWorkingMemoryRepository _workingMemoryRepository;
    private readonly ToolSchemaOrchestrator _toolSchemaOrchestrator;
    private readonly ICacheAlignmentService _cacheAlignment;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly IEffectiveSettingsAccessor _effectiveSettings;
    private readonly IOptionsMonitor<ProxyOptions> _proxyOptions;
    private readonly IOptionsMonitor<CacheAlignmentOptions> _cacheAlignmentOptions;
    private readonly ILogger<ClientHistorySynchronizer> _logger;

    public ClientHistorySynchronizer(
        IConversationMessageRepository messageRepository,
        IWorkingMemoryRepository workingMemoryRepository,
        ToolSchemaOrchestrator toolSchemaOrchestrator,
        ICacheAlignmentService cacheAlignment,
        ITokenEstimator tokenEstimator,
        IEffectiveSettingsAccessor effectiveSettings,
        IOptionsMonitor<ProxyOptions> proxyOptions,
        IOptionsMonitor<CacheAlignmentOptions> cacheAlignmentOptions,
        ILogger<ClientHistorySynchronizer> logger)
    {
        _messageRepository = messageRepository;
        _workingMemoryRepository = workingMemoryRepository;
        _toolSchemaOrchestrator = toolSchemaOrchestrator;
        _cacheAlignment = cacheAlignment;
        _tokenEstimator = tokenEstimator;
        _effectiveSettings = effectiveSettings;
        _proxyOptions = proxyOptions;
        _cacheAlignmentOptions = cacheAlignmentOptions;
        _logger = logger;
    }

    /// <summary>Test / legacy ctor (internal so MS DI sees only the public primary).</summary>
    internal ClientHistorySynchronizer(
        IConversationMessageRepository messageRepository,
        IWorkingMemoryRepository workingMemoryRepository,
        ToolSchemaOrchestrator toolSchemaOrchestrator,
        ICacheAlignmentService cacheAlignment,
        ITokenEstimator tokenEstimator,
        IOptions<ProxyOptions> proxyOptions,
        IOptions<CacheAlignmentOptions> cacheAlignmentOptions,
        ILogger<ClientHistorySynchronizer> logger)
        : this(
            messageRepository,
            workingMemoryRepository,
            toolSchemaOrchestrator,
            cacheAlignment,
            tokenEstimator,
            UnsetEffectiveSettingsAccessor.Instance,
            new FixedOptionsMonitor<ProxyOptions>(proxyOptions),
            new FixedOptionsMonitor<CacheAlignmentOptions>(cacheAlignmentOptions),
            logger)
    {
    }


    public void EnrichStoredMessagesFromClientHistory(
        List<ConversationMessage> storedMessages,
        IReadOnlyList<ChatMessage> clientMessages)
    {
        var orderedStored = storedMessages.OrderBy(m => m.Sequence).ToList();
        var clientNonSystem = clientMessages.Where(m => m.Role != MessageRole.System).ToList();
        var count = Math.Min(orderedStored.Count, clientNonSystem.Count);

        for (var i = 0; i < count; i++)
        {
            var stored = orderedStored[i];
            var client = clientNonSystem[i];
            if (stored.Role != client.Role)
            {
                break;
            }

            if (stored.HasWireJson && !string.IsNullOrWhiteSpace(stored.Content))
            {
                continue;
            }

            var wire = client.RawWireMessage?.GetRawText();
            if (string.IsNullOrWhiteSpace(wire) && string.IsNullOrWhiteSpace(client.Content))
            {
                continue;
            }

            var content = string.IsNullOrWhiteSpace(client.Content)
                ? ChatTurnMessageHelper.SummarizeAssistantContent(wire)
                : client.Content;
            var tokenCount = _tokenEstimator.CountTokens(new[] { client });
            stored.EnrichFromClient(content, wire, tokenCount);
        }
    }

    /// <summary>
    /// Discards persisted turns past the client snapshot and invalidates working-memory versions
    /// that absorbed any of those turns. Mutates <paramref name="storedMessages"/> in place.
    /// </summary>
    public async Task ApplyClientSnapshotRewindAsync(
        Conversation conversation,
        List<ConversationMessage> storedMessages,
        int keepNonSystemCount,
        Func<CancellationToken, Task> flushChatUnitAsync,
        CancellationToken cancellationToken)
    {
        if (keepNonSystemCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(keepNonSystemCount));
        }

        // Abandoned open IR→client rounds from the discarded branch must not block healing.
        var skipsOptimizations = _effectiveSettings.IsSet
            ? _effectiveSettings.Current.SkipsPromptOptimizations
            : _proxyOptions.CurrentValue.PassThrough;
        if (_toolSchemaOrchestrator.ShouldAttemptActivation(skipsOptimizations))
        {
            await _toolSchemaOrchestrator.ClearPendingToolCallMapsAsync(conversation.Id, cancellationToken);
        }

        var toDelete = storedMessages
            .Where(m => m.Sequence >= keepNonSystemCount)
            .OrderBy(m => m.Sequence)
            .ToList();

        int? invalidateWmFrom = null;
        foreach (var message in toDelete)
        {
            if (message.FoldedIntoWorkingMemoryVersion is int foldedVersion)
            {
                invalidateWmFrom = invalidateWmFrom is null
                    ? foldedVersion
                    : Math.Min(invalidateWmFrom.Value, foldedVersion);
            }
        }

        if (invalidateWmFrom is int fromVersion)
        {
            foreach (var kept in storedMessages.Where(m =>
                         m.Sequence < keepNonSystemCount &&
                         m.FoldedIntoWorkingMemoryVersion is int v &&
                         v >= fromVersion))
            {
                kept.ClearFold();
            }

            var removedWm = await _workingMemoryRepository.DeleteFromVersionAsync(
                conversation.Id,
                fromVersion,
                cancellationToken);
            _logger.LogInformation(
                "Snapshot rewind for conversation {ConversationId}: invalidated working memory from version {FromVersion} (deleted {DeletedCount} version row(s)).",
                conversation.Id,
                fromVersion,
                removedWm);
        }

        foreach (var message in toDelete)
        {
            _messageRepository.Remove(message);
            storedMessages.Remove(message);
        }

        if (toDelete.Count > 0)
        {
            _logger.LogInformation(
                "Snapshot rewind for conversation {ConversationId}: deleted {DeletedCount} stored message(s) from sequence {FromSequence} (keeping {KeepCount} non-system turn(s)).",
                conversation.Id,
                toDelete.Count,
                keepNonSystemCount,
                keepNonSystemCount);
        }

        if ((_effectiveSettings.IsSet ? _effectiveSettings.Current.CacheAlignmentEnabled : _cacheAlignmentOptions.CurrentValue.Enabled))
        {
            _cacheAlignment.Invalidate(conversation.Id);
        }

        await flushChatUnitAsync(cancellationToken);
    }
}
