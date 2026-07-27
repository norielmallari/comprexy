using Comprexy.Application.Models.Retrieval;

namespace Comprexy.Application.Abstractions;

/// <summary>
/// Read-only conversation retrieval (message / working-memory RAG) for control-api MCP.
/// </summary>
public interface IConversationRetrievalQueryService
{
    Task<bool> ConversationExistsAsync(Guid conversationId, CancellationToken cancellationToken);

    Task<ConversationSearchResultDto?> SearchAsync(
        Guid conversationId,
        string query,
        int? maxResults,
        bool includeFolded,
        bool includeWorkingMemory,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationMessageSnippetDto>?> GetMessageWindowAsync(
        Guid conversationId,
        int sequenceStart,
        int sequenceEnd,
        bool includeWireJson,
        int? maxMessages,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationMessageSnippetDto>?> GetRecentMessagesAsync(
        Guid conversationId,
        int? maxMessages,
        bool unfoldedOnly,
        bool includeWireJson,
        CancellationToken cancellationToken);

    Task<WorkingMemorySnapshotDto?> GetWorkingMemoryAsync(
        Guid conversationId,
        int? version,
        CancellationToken cancellationToken);

    Task<OpenToolChainsDto?> GetOpenToolChainsAsync(
        Guid conversationId,
        CancellationToken cancellationToken);
}
