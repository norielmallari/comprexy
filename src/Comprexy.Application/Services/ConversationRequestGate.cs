using System.Collections.Concurrent;
using Comprexy.Application.Abstractions;

namespace Comprexy.Application.Services;

/// <summary>
/// Process-wide keyed gate. Exclusive (chat) acquires cancel in-flight preemptible
/// (background soft compression) leases for the same key, then take the lock.
/// </summary>
public sealed class ConversationRequestGate : IConversationRequestGate
{
    private readonly ConcurrentDictionary<string, KeyState> _gates =
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

        var state = _gates.GetOrAdd(conversationKey, static _ => new KeyState());

        if (kind == ConversationGateLeaseKind.Exclusive)
        {
            CancelPreemptible(state);
        }

        await state.Lock.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (kind == ConversationGateLeaseKind.Preemptible)
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lock (state.Sync)
            {
                state.PreemptibleCts = linked;
            }

            return new PreemptibleLease(state, linked);
        }

        lock (state.Sync)
        {
            state.PreemptibleCts = null;
        }

        return new ExclusiveLease(state);
    }

    private static void CancelPreemptible(KeyState state)
    {
        CancellationTokenSource? cts;
        lock (state.Sync)
        {
            cts = state.PreemptibleCts;
            state.PreemptibleCts = null;
        }

        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Lease already released.
        }
    }

    private sealed class KeyState
    {
        public SemaphoreSlim Lock { get; } = new(1, 1);
        public object Sync { get; } = new();
        public CancellationTokenSource? PreemptibleCts;
    }

    private sealed class ExclusiveLease(KeyState state) : IConversationGateLease
    {
        private int _disposed;

        public CancellationToken Token => CancellationToken.None;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                state.Lock.Release();
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class PreemptibleLease(KeyState state, CancellationTokenSource linked) : IConversationGateLease
    {
        private int _disposed;

        public CancellationToken Token => linked.Token;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                lock (state.Sync)
                {
                    if (ReferenceEquals(state.PreemptibleCts, linked))
                    {
                        state.PreemptibleCts = null;
                    }
                }

                linked.Dispose();
                state.Lock.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
