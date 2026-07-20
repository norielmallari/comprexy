using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Models;
using Comprexy.Domain.Enums;

namespace Comprexy.Infrastructure.BackgroundJobs;

/// <summary>
/// In-memory compression queue backed by two unbounded channels so high-priority background jobs
/// (soft-limit-exceeded requests) are drained ahead of normal background jobs.
/// At most one pending job per conversation is kept (later enqueues coalesce).
/// </summary>
public class ChannelCompressionQueue : ICompressionQueue
{
    private readonly Channel<CompressionJob> _highPriority = Channel.CreateUnbounded<CompressionJob>();
    private readonly Channel<CompressionJob> _normal = Channel.CreateUnbounded<CompressionJob>();
    private readonly ConcurrentDictionary<Guid, byte> _pending = new();

    public void Enqueue(CompressionJob job)
    {
        if (!_pending.TryAdd(job.ConversationId, 0))
        {
            return;
        }

        var channel = job.Mode == CompressionMode.HighPriorityBackground ? _highPriority : _normal;
        if (!channel.Writer.TryWrite(job))
        {
            _pending.TryRemove(job.ConversationId, out _);
        }
    }

    public async IAsyncEnumerable<CompressionJob> DequeueAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_highPriority.Reader.TryRead(out var highPriorityJob))
            {
                _pending.TryRemove(highPriorityJob.ConversationId, out _);
                yield return highPriorityJob;
                continue;
            }

            if (_normal.Reader.TryRead(out var normalJob))
            {
                _pending.TryRemove(normalJob.ConversationId, out _);
                yield return normalJob;
                continue;
            }

            var highReady = _highPriority.Reader.WaitToReadAsync(cancellationToken).AsTask();
            var normalReady = _normal.Reader.WaitToReadAsync(cancellationToken).AsTask();
            await Task.WhenAny(highReady, normalReady);
        }
    }
}
