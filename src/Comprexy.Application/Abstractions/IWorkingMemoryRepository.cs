using Comprexy.Application.Models.Telemetry;
using Comprexy.Domain.Entities;

namespace Comprexy.Application.Abstractions;

public interface IWorkingMemoryRepository
{
    Task<WorkingMemory?> GetLatestAsync(Guid conversationId, CancellationToken cancellationToken);

    Task<WorkingMemory?> GetByVersionAsync(
        Guid conversationId,
        int version,
        CancellationToken cancellationToken);

    /// <summary>
    /// Substring match on <see cref="WorkingMemory.Content"/>, highest version first.
    /// Callers must clamp <paramref name="take"/>.
    /// </summary>
    Task<IReadOnlyList<WorkingMemory>> SearchContentAsync(
        Guid conversationId,
        string query,
        int take,
        CancellationToken cancellationToken);

    /// <summary>
    /// Version + token count for every version of a conversation, ascending, without content.
    /// </summary>
    Task<IReadOnlyList<WorkingMemoryVersionTokens>> ListVersionTokenCountsAsync(
        Guid conversationId,
        CancellationToken cancellationToken);

    void Add(WorkingMemory workingMemory);

    /// <summary>
    /// Deletes working-memory versions at or above <paramref name="fromVersionInclusive"/>
    /// (snapshot rewind invalidated those summaries).
    /// </summary>
    Task<int> DeleteFromVersionAsync(
        Guid conversationId,
        int fromVersionInclusive,
        CancellationToken cancellationToken);
}
