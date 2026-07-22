using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services;

/// <summary>
/// Runs a single compression attempt: folds older raw messages into an updated working memory
/// version using the compression model. Soft jobs prefer a full-raw rebuild when the transcript
/// fits <see cref="ContextPolicyOptions.CompressionMaxInputTokens"/>; otherwise they merge into
/// existing working memory. Soft Smart retain reuses the live chat message prefix plus a trailing
/// retain-index instruction (KV-cache friendly). Emergency jobs always use the bounded Fixed
/// merge path. Failures never touch the last known-good working memory.
/// </summary>
public class CompressionOrchestrator : ICompressionOrchestrator
{
    private readonly IConversationRepository _conversationRepository;
    private readonly IConversationMessageRepository _messageRepository;
    private readonly IWorkingMemoryRepository _workingMemoryRepository;
    private readonly ICompressionEventRepository _compressionEventRepository;
    private readonly IChatCompletionClient _chatCompletionClient;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly ProviderEndpointResolver _endpointResolver;
    private readonly CompressionPromptFactory _promptFactory;
    private readonly ContextBuilder _contextBuilder;
    private readonly RecentContextSelector _recentContextSelector;
    private readonly ContextPolicyOptions _policy;
    private readonly CompressionOptions _compressionOptions;
    private readonly ILogger<CompressionOrchestrator> _logger;

    public CompressionOrchestrator(
        IConversationRepository conversationRepository,
        IConversationMessageRepository messageRepository,
        IWorkingMemoryRepository workingMemoryRepository,
        ICompressionEventRepository compressionEventRepository,
        IChatCompletionClient chatCompletionClient,
        ITokenEstimator tokenEstimator,
        IUnitOfWork unitOfWork,
        IClock clock,
        ProviderEndpointResolver endpointResolver,
        CompressionPromptFactory promptFactory,
        ContextBuilder contextBuilder,
        RecentContextSelector recentContextSelector,
        IOptions<ContextPolicyOptions> policy,
        IOptions<CompressionOptions> compressionOptions,
        ILogger<CompressionOrchestrator> logger)
    {
        _conversationRepository = conversationRepository;
        _messageRepository = messageRepository;
        _workingMemoryRepository = workingMemoryRepository;
        _compressionEventRepository = compressionEventRepository;
        _chatCompletionClient = chatCompletionClient;
        _tokenEstimator = tokenEstimator;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _endpointResolver = endpointResolver;
        _promptFactory = promptFactory;
        _contextBuilder = contextBuilder;
        _recentContextSelector = recentContextSelector;
        _policy = policy.Value;
        _compressionOptions = compressionOptions.Value;
        _logger = logger;
    }

    public async Task<CompressionEvent?> RunAsync(Guid conversationId, CompressionMode mode, CancellationToken cancellationToken)
    {
        var conversation = await _conversationRepository.FindByIdAsync(conversationId, cancellationToken);
        if (conversation is null)
        {
            _logger.LogWarning("compression skipped: conversation {ConversationId} not found.", conversationId);
            return null;
        }

        var allMessages = await _messageRepository.GetByConversationIdAsync(conversationId, cancellationToken);
        var unfolded = allMessages.Where(m => !m.IsFolded).OrderBy(m => m.Sequence).ToList();

        var openTools = ToolCallChainState.Assess(unfolded);
        if (openTools.IsOpen)
        {
            _logger.LogInformation(
                "compression skipped: open tool calls conversationId={ConversationId} mode={Mode} unmatchedCount={UnmatchedCount}",
                conversationId,
                mode,
                openTools.UnmatchedCount);
            return null;
        }

        var retainCount = mode == CompressionMode.Emergency
            ? _policy.EmergencyRecentMessageCount
            : _policy.CompressionRetainMessageCount;
        var keepRecent = _recentContextSelector.Select(unfolded, maxMessagesOverride: retainCount).ToList();

        if (unfolded.Count <= keepRecent.Count)
        {
            _logger.LogInformation(
                "compression skipped: conversation {ConversationId} mode={Mode} unfoldedCount={UnfoldedCount} retainCount={RetainCount} (nothing to fold).",
                conversationId,
                mode,
                unfolded.Count,
                retainCount);
            return null;
        }

        var existingWorkingMemory = await _workingMemoryRepository.GetLatestAsync(conversationId, cancellationToken);
        var rawTokens = allMessages.Sum(m => Math.Max(0, m.TokenCount));
        var maxInput = Math.Max(1, _policy.CompressionMaxInputTokens);
        var useSmart = mode != CompressionMode.Emergency &&
                       _policy.RetainSelection == RetainSelectionMode.Smart;
        var useFullRaw = mode != CompressionMode.Emergency && rawTokens <= maxInput;
        var tip = SmartRetainResolver.FindForcedTip(unfolded);

        var promptCorpus = (IReadOnlyList<ConversationMessage>)(useFullRaw ? allMessages : unfolded);
        var droppedByDedupe = new HashSet<int>();

        if (mode != CompressionMode.Emergency && _policy.DedupeDuplicateFileReads)
        {
            var dedupe = DuplicateFileReadDeduper.Apply(promptCorpus, promptCorpus, tip?.Sequence);
            promptCorpus = dedupe.Retain;
            droppedByDedupe = dedupe.DroppedSequences.ToHashSet();

            if (dedupe.DroppedAny)
            {
                _logger.LogInformation(
                    "duplicate_file_read_dedupe conversationId={ConversationId} phase=pre_llm droppedCount={DroppedCount} keptPaths={KeptPaths} droppedSequences={DroppedSequences}",
                    conversationId,
                    dedupe.DroppedSequences.Count,
                    string.Join(',', dedupe.KeptPaths),
                    string.Join(',', dedupe.DroppedSequences));

                keepRecent = keepRecent.Where(m => !droppedByDedupe.Contains(m.Sequence)).ToList();
                var (sanitizedKeep, droppedOrphans) = ChatTemplateMessageOrder.RemoveOrphanToolMessages(keepRecent);
                keepRecent = sanitizedKeep.ToList();
                if (droppedOrphans > 0)
                {
                    _logger.LogWarning(
                        "duplicate_file_read_dedupe conversationId={ConversationId} dropped {DroppedCount} orphan tool message(s) from Fixed retain tip.",
                        conversationId,
                        droppedOrphans);
                }

                if (keepRecent.Count == 0 && tip is not null)
                {
                    keepRecent = [tip];
                }
            }
        }

        if (unfolded.Count <= keepRecent.Count)
        {
            _logger.LogInformation(
                "compression skipped: conversation {ConversationId} mode={Mode} unfoldedCount={UnfoldedCount} retainCount={RetainCount} (nothing to fold after pre-llm dedupe).",
                conversationId,
                mode,
                unfolded.Count,
                keepRecent.Count);
            return null;
        }

        var keepIds = keepRecent.Select(m => m.Id).ToHashSet();
        List<ConversationMessage> fixedFoldSet;
        IReadOnlyList<ChatMessage> promptMessages;
        string strategy;
        IReadOnlyList<ConversationMessage> promptBodyMessages;

        if (useFullRaw)
        {
            strategy = "FullRaw";
            fixedFoldSet = allMessages.Where(m => !keepIds.Contains(m.Id)).OrderBy(m => m.Sequence).ToList();
            if (fixedFoldSet.Count == 0)
            {
                _logger.LogInformation(
                    "compression skipped: conversation {ConversationId} mode={Mode} strategy={Strategy} (nothing to fold after retain tip).",
                    conversationId,
                    mode,
                    strategy);
                return null;
            }

            promptBodyMessages = promptCorpus;
            if (useSmart)
            {
                promptMessages = BuildSmartLivePrefixPrompt(
                    conversation.SystemPrompt,
                    existingWorkingMemory,
                    promptCorpus,
                    tip?.Sequence);
            }
            else
            {
                promptMessages = _promptFactory.BuildMessagesFromFullRaw(promptCorpus);
            }
        }
        else
        {
            strategy = "WorkingMemoryMerge";
            fixedFoldSet = unfolded.Where(m => !keepIds.Contains(m.Id)).OrderBy(m => m.Sequence).ToList();
            fixedFoldSet = ShrinkFoldSetToCap(existingWorkingMemory, fixedFoldSet, maxInput);

            if (fixedFoldSet.Count == 0)
            {
                _logger.LogInformation(
                    "compression skipped: conversation {ConversationId} mode={Mode} strategy={Strategy} rawTokens={RawTokens} compressionMaxInputTokens={CompressionMaxInputTokens} (fold set empty after shrink).",
                    conversationId,
                    mode,
                    strategy,
                    rawTokens,
                    maxInput);
                return null;
            }

            if (useSmart)
            {
                var transcriptUnfolded = FitUnfoldedForSmartPrompt(
                    existingWorkingMemory,
                    promptCorpus.OrderBy(m => m.Sequence).ToList(),
                    maxInput);
                promptBodyMessages = transcriptUnfolded;
                promptMessages = BuildSmartLivePrefixPrompt(
                    conversation.SystemPrompt,
                    existingWorkingMemory,
                    transcriptUnfolded,
                    tip?.Sequence);
            }
            else
            {
                var promptFoldSet = fixedFoldSet
                    .Where(m => !droppedByDedupe.Contains(m.Sequence))
                    .ToList();
                promptBodyMessages = promptFoldSet;
                promptMessages = _promptFactory.BuildMessages(existingWorkingMemory, promptFoldSet);
            }
        }

        var inputTokens = useSmart
            ? EstimatePromptBodyTokens(existingWorkingMemory, promptBodyMessages, fullRawTranscript: false)
            : EstimatePromptBodyTokens(
                useFullRaw ? null : existingWorkingMemory,
                promptBodyMessages,
                useFullRaw);

        _logger.LogInformation(
            "compression_strategy={Strategy} conversationId={ConversationId} mode={Mode} retainSelection={RetainSelection} compressionMaxInputTokens={CompressionMaxInputTokens} rawTokens={RawTokens} inputTokens={InputTokens} provisionalFoldCount={FoldCount}",
            strategy,
            conversationId,
            mode,
            useSmart ? "Smart" : "Fixed",
            maxInput,
            rawTokens,
            inputTokens,
            fixedFoldSet.Count);

        var now = _clock.UtcNow;
        var compressionEvent = CompressionEvent.Start(
            conversationId,
            mode,
            inputTokens,
            existingWorkingMemory?.Version,
            fixedFoldSet.Count,
            now);
        _compressionEventRepository.Add(compressionEvent);

        try
        {
            // Prefer the chat endpoint when it shares KV cache with compression (same host+model).
            var compressionEndpoint = _endpointResolver.ResolveCompression();
            var chatEndpoint = _endpointResolver.ResolveUpstream();
            var endpoint = useSmart && chatEndpoint.SharesKvCacheWith(compressionEndpoint)
                ? chatEndpoint
                : compressionEndpoint;
            var callOptions = new ChatCompletionCallOptions(Temperature: _compressionOptions.Temperature);

            var result = await _chatCompletionClient.CompleteAsync(
                endpoint,
                new UpstreamRequest(
                    promptMessages,
                    Stream: false,
                    CallOptions: callOptions,
                    Purpose: UpstreamRequestPurpose.Compression),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            string workingMemoryContent;
            List<ConversationMessage> messagesToFold;
            var retainSelectionLabel = useSmart ? "Smart" : "Fixed";
            var foldUniverse = useFullRaw ? allMessages : unfolded;

            if (useSmart)
            {
                var parsed = SmartCompressionResultParser.Parse(result.Content);
                if (!parsed.HasWorkingMemory)
                {
                    throw new InvalidOperationException("Smart compression returned no usable working memory.");
                }

                workingMemoryContent = parsed.WorkingMemory;

                if (!parsed.HasRetainList)
                {
                    retainSelectionLabel = "FixedFallback";
                    messagesToFold = fixedFoldSet;
                    _logger.LogInformation(
                        "retain_selection={RetainSelection} conversationId={ConversationId}",
                        retainSelectionLabel,
                        conversationId);
                }
                else
                {
                    var retain = SmartRetainResolver.Resolve(
                        parsed.RetainSequences,
                        foldUniverse,
                        tip,
                        _policy.SmartRetainMaxMessages,
                        _policy.SmartRetainMaxTokens);

                    var retainSequences = retain.Select(m => m.Sequence).ToHashSet();
                    messagesToFold = foldUniverse
                        .Where(m => !retainSequences.Contains(m.Sequence))
                        .OrderBy(m => m.Sequence)
                        .ToList();

                    _logger.LogInformation(
                        "retain_selection={RetainSelection} conversationId={ConversationId} nominatedCount={NominatedCount} acceptedCount={AcceptedCount} foldCount={FoldCount}",
                        retainSelectionLabel,
                        conversationId,
                        parsed.RetainSequences!.Count,
                        retain.Count,
                        messagesToFold.Count);
                }
            }
            else
            {
                workingMemoryContent = result.Content;
                messagesToFold = fixedFoldSet;
            }

            if (!WorkingMemorySanityChecker.TryAccept(
                    workingMemoryContent,
                    out var acceptedWorkingMemory,
                    out var rejectionReason))
            {
                _logger.LogWarning(
                    "compression_working_memory_rejected conversationId={ConversationId} mode={Mode} reason={Reason}",
                    conversationId,
                    mode,
                    rejectionReason);
                throw new InvalidOperationException(
                    $"Compression returned invalid working memory ({rejectionReason}).");
            }

            workingMemoryContent = acceptedWorkingMemory;

            if (messagesToFold.Count == 0)
            {
                throw new InvalidOperationException("compression produced an empty fold set.");
            }

            var compressedTokens = _tokenEstimator.CountTokens(workingMemoryContent);
            var newVersion = (existingWorkingMemory?.Version ?? 0) + 1;
            var newWorkingMemory = WorkingMemory.Create(
                conversationId,
                newVersion,
                workingMemoryContent,
                compressedTokens,
                _clock.UtcNow);
            _workingMemoryRepository.Add(newWorkingMemory);

            foreach (var message in messagesToFold)
            {
                message.MarkFoldedInto(newVersion);
            }

            compressionEvent.Succeed(compressedTokens, newVersion, _clock.UtcNow);

            _logger.LogInformation(
                "context_compression_completed conversationId={ConversationId} mode={Mode} strategy={Strategy} retainSelection={RetainSelection} originalTokens={OriginalTokens} compressedTokens={CompressedTokens} compressionRatio={CompressionRatio} durationMs={DurationMs} workingMemoryVersion={WorkingMemoryVersion} foldCount={FoldCount}",
                conversationId,
                mode,
                strategy,
                retainSelectionLabel,
                compressionEvent.OriginalTokens,
                compressedTokens,
                compressionEvent.CompressionRatio,
                compressionEvent.DurationMs,
                newVersion,
                messagesToFold.Count);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (compressionEvent.Status == CompressionStatus.Succeeded)
            {
                await _unitOfWork.SaveChangesAsync(CancellationToken.None);
            }
            else
            {
                compressionEvent.Fail("cancelled", _clock.UtcNow);
                _logger.LogInformation(
                    "compression_cancelled conversationId={ConversationId} mode={Mode} reason=client_preempt",
                    conversationId,
                    mode);
                await _unitOfWork.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            compressionEvent.Fail(ex.Message, _clock.UtcNow);
            _logger.LogError(ex, "context_compression_failed conversationId={ConversationId} mode={Mode}", conversationId, mode);
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        return compressionEvent;
    }

    /// <summary>
    /// Live chat-shaped prefix (system + WM + raw messages) plus trailing Smart instruction
    /// and retain index. Tip policy: current DB unfolded window (post-fit), including the latest
    /// tip — matches post-response state for oMLX prefix reuse through stable older turns.
    /// </summary>
    private IReadOnlyList<ChatMessage> BuildSmartLivePrefixPrompt(
        string? systemPrompt,
        WorkingMemory? existingWorkingMemory,
        IReadOnlyList<ConversationMessage> prefixMessages,
        int? tipSequence)
    {
        var prefix = _contextBuilder.BuildLivePrefix(systemPrompt, existingWorkingMemory, prefixMessages);
        var retainIndex = RetainIndexBuilder.Build(prefixMessages, tipSequence);
        var instruction = _promptFactory.BuildSmartInstructionMessage(retainIndex);
        return prefix.Append(instruction).ToList();
    }

    private List<ConversationMessage> FitUnfoldedForSmartPrompt(
        WorkingMemory? workingMemory,
        List<ConversationMessage> unfolded,
        int maxInputTokens)
    {
        var wmTokens = _tokenEstimator.CountTokens(workingMemory?.Content ?? string.Empty);
        if (wmTokens > maxInputTokens || unfolded.Count == 0)
        {
            return unfolded.Count == 0 ? unfolded : [unfolded[^1]];
        }

        var tip = SmartRetainResolver.FindForcedTip(unfolded) ?? unfolded[^1];
        var selected = new List<ConversationMessage> { tip };
        var running = wmTokens + Math.Max(0, tip.TokenCount);

        for (var i = unfolded.Count - 1; i >= 0; i--)
        {
            var message = unfolded[i];
            if (message.Sequence == tip.Sequence)
            {
                continue;
            }

            var next = Math.Max(0, message.TokenCount);
            if (running + next > maxInputTokens)
            {
                continue;
            }

            selected.Add(message);
            running += next;
        }

        return selected.OrderBy(m => m.Sequence).ToList();
    }

    private List<ConversationMessage> ShrinkFoldSetToCap(
        WorkingMemory? workingMemory,
        List<ConversationMessage> messagesToFold,
        int maxInputTokens)
    {
        var wmTokens = _tokenEstimator.CountTokens(workingMemory?.Content ?? string.Empty);
        if (wmTokens > maxInputTokens)
        {
            return [];
        }

        var shrunk = new List<ConversationMessage>();
        var running = wmTokens;
        foreach (var message in messagesToFold)
        {
            var next = Math.Max(0, message.TokenCount);
            if (running + next > maxInputTokens)
            {
                break;
            }

            shrunk.Add(message);
            running += next;
        }

        return shrunk;
    }

    private int EstimatePromptBodyTokens(
        WorkingMemory? workingMemory,
        IReadOnlyList<ConversationMessage> messages,
        bool fullRawTranscript)
    {
        var messageTokens = messages.Sum(m => Math.Max(0, m.TokenCount));
        if (fullRawTranscript)
        {
            return messageTokens;
        }

        var workingMemoryTokens = _tokenEstimator.CountTokens(workingMemory?.Content ?? string.Empty);
        return workingMemoryTokens + messageTokens;
    }
}
