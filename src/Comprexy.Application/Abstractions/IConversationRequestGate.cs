namespace Comprexy.Application.Abstractions;

/// <summary>
/// How a conversation gate lease behaves under contention.
/// </summary>
public enum ConversationGateLeaseKind
{
    /// <summary>
    /// Chat prepare/complete (including emergency compression). Preempts any in-flight
    /// <see cref="Preemptible"/> lease for the same key.
    /// </summary>
    Exclusive = 0,

    /// <summary>
    /// Background soft compression when <c>CancelBackgroundCompressionOnChat</c> is true.
    /// Cancelled when an <see cref="Exclusive"/> acquire starts for the same conversation key.
    /// </summary>
    Preemptible = 1
}

/// <summary>
/// Exclusive or preemptible lease for a conversation key. Dispose to release.
/// </summary>
public interface IConversationGateLease : IAsyncDisposable
{
    /// <summary>
    /// For <see cref="ConversationGateLeaseKind.Preemptible"/> leases, cancelled when chat
    /// preempts. For exclusive leases, <see cref="CancellationToken.None"/>.
    /// </summary>
    CancellationToken Token { get; }
}

/// <summary>
/// Serializes work that mutates conversation state (chat prepare/complete and compression)
/// so concurrent requests or background jobs for the same conversation key cannot race.
/// Soft background work may use a preemptible lease (cancelled by chat) or an exclusive lease
/// (chat waits), controlled by <c>ContextPolicy:CancelBackgroundCompressionOnChat</c>.
/// </summary>
public interface IConversationRequestGate
{
    /// <summary>
    /// Acquires a lease for <paramref name="conversationKey"/>. Dispose the lease to release.
    /// Chat handlers use <see cref="ConversationGateLeaseKind.Exclusive"/>; background soft
    /// compression uses <see cref="ConversationGateLeaseKind.Preemptible"/> when cancel-on-chat
    /// is enabled, otherwise <see cref="ConversationGateLeaseKind.Exclusive"/>.
    /// </summary>
    Task<IConversationGateLease> AcquireAsync(
        string conversationKey,
        ConversationGateLeaseKind kind,
        CancellationToken cancellationToken);
}
