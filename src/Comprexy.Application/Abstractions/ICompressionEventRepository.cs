using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Abstractions;

public interface ICompressionEventRepository
{
    void Add(CompressionEvent compressionEvent);

    Task<CompressionEvent?> GetLatestSucceededAsync(
        Guid conversationId,
        CompressionMode mode,
        CancellationToken cancellationToken);
}
