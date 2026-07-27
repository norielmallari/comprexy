using Comprexy.Application.Abstractions;
using Comprexy.Application.Models.Retrieval;
using Comprexy.Application.Models.Telemetry;
using Comprexy.Domain.Entities;

namespace Comprexy.Application.Services;

public sealed class ConversationRetrievalQueryService : IConversationRetrievalQueryService
{
    private readonly IConversationRepository _conversationRepository;
    private readonly IConversationMessageRepository _messageRepository;
    private readonly IWorkingMemoryRepository _workingMemoryRepository;

    public ConversationRetrievalQueryService(
        IConversationRepository conversationRepository,
        IConversationMessageRepository messageRepository,
        IWorkingMemoryRepository workingMemoryRepository)
    {
        _conversationRepository = conversationRepository;
        _messageRepository = messageRepository;
        _workingMemoryRepository = workingMemoryRepository;
    }

    public Task<bool> ConversationExistsAsync(Guid conversationId, CancellationToken cancellationToken) =>
        _conversationRepository.ExistsAsync(conversationId, cancellationToken);

    public async Task<ConversationSearchResultDto?> SearchAsync(
        Guid conversationId,
        string query,
        int? maxResults,
        bool includeFolded,
        bool includeWorkingMemory,
        CancellationToken cancellationToken)
    {
        if (!await _conversationRepository.ExistsAsync(conversationId, cancellationToken))
        {
            return null;
        }

        var normalizedQuery = query.Trim();
        if (normalizedQuery.Length == 0)
        {
            throw new ArgumentException("Search query must not be empty.", nameof(query));
        }

        var take = TelemetryQueryLimits.ClampTake(maxResults);
        // Fetch a bit extra so WM + message ranking can still fill the limit.
        var fetchTake = TelemetryQueryLimits.ClampTake(take * 2);

        var matches = new List<ConversationSearchMatchDto>();

        if (includeWorkingMemory)
        {
            var memories = await _workingMemoryRepository.SearchContentAsync(
                conversationId,
                normalizedQuery,
                fetchTake,
                cancellationToken);
            foreach (var memory in memories.OrderByDescending(m => m.Version))
            {
                matches.Add(new ConversationSearchMatchDto
                {
                    SourceType = "working_memory",
                    WorkingMemoryVersion = memory.Version,
                    Text = RetrievalQueryLimits.Truncate(memory.Content)
                });
            }
        }

        var messages = await _messageRepository.SearchContentAsync(
            conversationId,
            normalizedQuery,
            includeFolded,
            fetchTake,
            cancellationToken);

        foreach (var message in messages.OrderByDescending(m => m.Sequence))
        {
            matches.Add(MapSearchMatch(message));
        }

        // Prefer working memory, then newer messages (already ordered within each group).
        var ranked = matches
            .OrderBy(m => m.SourceType == "working_memory" ? 0 : 1)
            .ThenByDescending(m => m.WorkingMemoryVersion ?? m.Sequence ?? 0)
            .Take(take)
            .ToList();

        return new ConversationSearchResultDto
        {
            ConversationId = conversationId,
            Query = normalizedQuery,
            Matches = ranked
        };
    }

    public async Task<IReadOnlyList<ConversationMessageSnippetDto>?> GetMessageWindowAsync(
        Guid conversationId,
        int sequenceStart,
        int sequenceEnd,
        bool includeWireJson,
        int? maxMessages,
        CancellationToken cancellationToken)
    {
        if (!await _conversationRepository.ExistsAsync(conversationId, cancellationToken))
        {
            return null;
        }

        if (sequenceStart < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequenceStart), "Sequence start must be >= 0.");
        }

        if (sequenceEnd < sequenceStart)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequenceEnd),
                "Sequence end must be >= sequence start.");
        }

        var take = TelemetryQueryLimits.ClampTake(maxMessages);
        var span = sequenceEnd - sequenceStart + 1;
        if (span > take)
        {
            sequenceEnd = sequenceStart + take - 1;
        }

        var messages = await _messageRepository.ListBySequenceRangeAsync(
            conversationId,
            sequenceStart,
            sequenceEnd,
            take,
            cancellationToken);

        return messages.Select(m => MapSnippet(m, includeWireJson)).ToList();
    }

    public async Task<IReadOnlyList<ConversationMessageSnippetDto>?> GetRecentMessagesAsync(
        Guid conversationId,
        int? maxMessages,
        bool unfoldedOnly,
        bool includeWireJson,
        CancellationToken cancellationToken)
    {
        if (!await _conversationRepository.ExistsAsync(conversationId, cancellationToken))
        {
            return null;
        }

        var take = TelemetryQueryLimits.ClampTake(maxMessages);
        var messages = await _messageRepository.ListRecentAsync(
            conversationId,
            take,
            unfoldedOnly,
            cancellationToken);

        return messages.Select(m => MapSnippet(m, includeWireJson)).ToList();
    }

    public async Task<WorkingMemorySnapshotDto?> GetWorkingMemoryAsync(
        Guid conversationId,
        int? version,
        CancellationToken cancellationToken)
    {
        if (!await _conversationRepository.ExistsAsync(conversationId, cancellationToken))
        {
            return null;
        }

        WorkingMemory? memory;
        if (version is null)
        {
            memory = await _workingMemoryRepository.GetLatestAsync(conversationId, cancellationToken);
        }
        else
        {
            if (version.Value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(version), "Working memory version must be >= 1.");
            }

            memory = await _workingMemoryRepository.GetByVersionAsync(
                conversationId,
                version.Value,
                cancellationToken);
        }

        return memory is null ? null : MapWorkingMemory(memory);
    }

    public async Task<OpenToolChainsDto?> GetOpenToolChainsAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        if (!await _conversationRepository.ExistsAsync(conversationId, cancellationToken))
        {
            return null;
        }

        var unfolded = await _messageRepository.GetUnfoldedAsync(conversationId, cancellationToken);
        var assessment = ToolCallChainState.Assess(unfolded);
        return new OpenToolChainsDto
        {
            ConversationId = conversationId,
            IsOpen = assessment.IsOpen,
            UnmatchedCount = assessment.UnmatchedCount,
            OpenToolCallIds = assessment.OpenToolCallIds
        };
    }

    private static ConversationSearchMatchDto MapSearchMatch(ConversationMessage message) =>
        new()
        {
            SourceType = "message",
            Sequence = message.Sequence,
            Role = message.Role.ToString().ToLowerInvariant(),
            IsFolded = message.IsFolded,
            Text = RetrievalQueryLimits.Truncate(message.Content)
        };

    private static ConversationMessageSnippetDto MapSnippet(
        ConversationMessage message,
        bool includeWireJson) =>
        new()
        {
            Sequence = message.Sequence,
            Role = message.Role.ToString().ToLowerInvariant(),
            Text = RetrievalQueryLimits.Truncate(message.Content),
            TokenCount = message.TokenCount,
            IsFolded = message.IsFolded,
            FoldedIntoWorkingMemoryVersion = message.FoldedIntoWorkingMemoryVersion,
            IsPinnedForToolSchema = message.IsPinnedForToolSchema,
            RawWireJson = includeWireJson
                ? RetrievalQueryLimits.Truncate(
                    message.RawWireJson,
                    RetrievalQueryLimits.DefaultMaxWireJsonChars)
                : null
        };

    private static WorkingMemorySnapshotDto MapWorkingMemory(WorkingMemory memory) =>
        new()
        {
            ConversationId = memory.ConversationId,
            Version = memory.Version,
            Content = memory.Content,
            TokenCount = memory.TokenCount,
            CreatedAt = memory.CreatedAt
        };
}
