using Comprexy.Application.Models;

namespace Comprexy.Application.Abstractions;

/// <summary>
/// Non-blocking queue of compression jobs consumed by the background worker.
/// Implementations may coalesce multiple enqueues for the same conversation into one pending job.
/// </summary>
public interface ICompressionQueue
{
    /// <summary>
    /// Queues a compression job. Duplicate conversation ids that are already pending are ignored.
    /// </summary>
    void Enqueue(CompressionJob job);

    IAsyncEnumerable<CompressionJob> DequeueAllAsync(CancellationToken cancellationToken);
}
