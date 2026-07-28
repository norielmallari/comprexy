namespace Comprexy.Application.Abstractions;

/// <summary>
/// How a conversation gate lease behaves under contention.
/// </summary>
public enum ConversationGateLeaseKind
{
    /// <summary>
    /// Chat prepare/complete (including Inline wrap-up). Serializes work for a conversation key.
    /// </summary>
    Exclusive = 0
}

/// <summary>
/// Exclusive lease for a conversation key. Dispose to release.
/// </summary>
public interface IConversationGateLease : IAsyncDisposable
{
    /// <summary>
    /// Always <see cref="CancellationToken.None"/> for exclusive leases.
    /// </summary>
    CancellationToken Token { get; }
}

/// <summary>
/// Serializes work that mutates conversation state (chat prepare/complete including Inline wrap-up)
/// so concurrent requests for the same conversation key cannot race.
/// </summary>
public interface IConversationRequestGate
{
    /// <summary>
    /// Acquires an exclusive lease for <paramref name="conversationKey"/>. Dispose the lease to release.
    /// </summary>
    Task<IConversationGateLease> AcquireAsync(
        string conversationKey,
        ConversationGateLeaseKind kind,
        CancellationToken cancellationToken);
}
