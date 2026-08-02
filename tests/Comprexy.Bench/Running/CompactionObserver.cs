using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.Logging;

namespace Comprexy.Bench.Running;

/// <summary>
/// Wraps the MAF client-side compaction strategy so the harness can record how often it actually
/// removed context. On the treatment arm this firing is a validity signal, not a headline number:
/// it means the prompt still reached the client window despite Comprexy.
/// </summary>
internal sealed class CompactionObserver : CompactionStrategy
{
    private readonly CompactionStrategy _inner;
    private int _appliedCount;

    public CompactionObserver(CompactionStrategy inner)
        : base(CompactionTriggers.Always, null)
    {
        _inner = inner;
    }

    public int AppliedCount => Volatile.Read(ref _appliedCount);

    protected override async ValueTask<bool> CompactCoreAsync(
        CompactionMessageIndex index,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var applied = await _inner.CompactAsync(index, logger, cancellationToken);
        if (applied)
        {
            Interlocked.Increment(ref _appliedCount);
        }

        return applied;
    }
}
