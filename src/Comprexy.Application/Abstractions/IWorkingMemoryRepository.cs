using Comprexy.Domain.Entities;

namespace Comprexy.Application.Abstractions;

public interface IWorkingMemoryRepository
{
    Task<WorkingMemory?> GetLatestAsync(Guid conversationId, CancellationToken cancellationToken);

    void Add(WorkingMemory workingMemory);
}
