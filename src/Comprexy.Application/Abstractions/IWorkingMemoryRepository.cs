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

    void Add(WorkingMemory workingMemory);
}
