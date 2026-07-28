using System.Collections.Concurrent;
using Comprexy.Application.Abstractions;

namespace Comprexy.Application.Services;

/// <summary>
/// Process-wide keyed exclusive gate for chat prepare/complete (including Inline wrap-up).
/// </summary>
public sealed class ConversationRequestGate : IConversationRequestGate
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates =
        new(StringComparer.Ordinal);

    public async Task<IConversationGateLease> AcquireAsync(
        string conversationKey,
        ConversationGateLeaseKind kind,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(conversationKey))
        {
            throw new ArgumentException("Conversation key must not be empty.", nameof(conversationKey));
        }

        if (kind != ConversationGateLeaseKind.Exclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Only exclusive leases are supported.");
        }

        var gate = _gates.GetOrAdd(conversationKey, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new ExclusiveLease(gate);
    }

    private sealed class ExclusiveLease(SemaphoreSlim gate) : IConversationGateLease
    {
        private int _disposed;

        public CancellationToken Token => CancellationToken.None;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                gate.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
