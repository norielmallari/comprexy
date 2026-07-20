namespace Comprexy.Application.Abstractions;

/// <summary>
/// In-memory cache for individual token estimates. Keys are SHA-256 hashes of the
/// serialized input; values are cached token counts with TTL-based expiration.
/// </summary>
public interface ITokenEstimateCache
{
    /// <summary>
    /// Returns a cached token estimate for <paramref name="key"/>, or computes and stores
    /// the result via <paramref name="compute"/> on a cache miss.
    /// </summary>
    int GetOrCompute(string key, Func<int> compute, CancellationToken cancellationToken = default);
}
