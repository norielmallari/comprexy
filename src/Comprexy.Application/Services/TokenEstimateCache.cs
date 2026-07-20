using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services;

/// <summary>
/// Dedicated in-memory cache for token estimates. Owns a private <see cref="MemoryCache"/>
/// (never the process-shared DI cache). Keys are namespaced SHA-256 hashes.
/// </summary>
public sealed class TokenEstimateCache : ITokenEstimateCache, IDisposable
{
    private const int EntrySize = 1;

    private readonly IMemoryCache _cache;
    private readonly bool _ownsCache;
    private readonly TokenEstimateCacheOptions _options;
    private readonly ConcurrentDictionary<string, object> _locks = new();
    private bool _disposed;

    public TokenEstimateCache(IOptions<TokenEstimateCacheOptions> options)
        : this(CreateOwnedCache(options.Value), options, ownsCache: true)
    {
    }

    /// <summary>
    /// Allows injecting a cache instance (tests). When <paramref name="ownsCache"/> is false,
    /// <see cref="Dispose"/> does not dispose <paramref name="cache"/>.
    /// </summary>
    public TokenEstimateCache(IMemoryCache cache, IOptions<TokenEstimateCacheOptions> options, bool ownsCache = false)
    {
        _cache = cache;
        _ownsCache = ownsCache;
        _options = options.Value;
    }

    private static MemoryCache CreateOwnedCache(TokenEstimateCacheOptions options) =>
        new(new MemoryCacheOptions
        {
            SizeLimit = options.SizeLimit ?? TokenEstimateCacheOptions.DefaultSizeLimit
        });

    public int GetOrCompute(string key, Func<int> compute, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (_cache.TryGetValue(key, out int cachedValue))
        {
            return cachedValue;
        }

        var lockObj = _locks.GetOrAdd(key, static _ => new object());
        try
        {
            lock (lockObj)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_cache.TryGetValue(key, out int doubleCheckValue))
                {
                    return doubleCheckValue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var tokenCount = compute();

                var entryOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = _options.AbsoluteExpiration,
                    Size = EntrySize
                };

                _cache.Set(key, tokenCount, entryOptions);
                return tokenCount;
            }
        }
        finally
        {
            // Conditional remove: only drop the lock if it is still ours.
            if (_locks.TryGetValue(key, out var current) && ReferenceEquals(current, lockObj))
            {
                _locks.TryRemove(KeyValuePair.Create(key, lockObj));
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsCache)
        {
            _cache.Dispose();
        }
    }

    /// <summary>SHA-256 key for plain text / JSON side payloads (<c>s:</c> namespace).</summary>
    public static string ComputeStringKey(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return "s:" + Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// SHA-256 key for one message, matching <c>TiktokenTokenEstimator</c> wire preference
    /// (Object wire → <c>m:w:</c>, else content → <c>m:c:</c>).
    /// </summary>
    public static string ComputeMessageKey(ChatMessage message)
    {
        if (message.RawWireMessage is { ValueKind: JsonValueKind.Object } raw)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw.GetRawText()));
            return "m:w:" + Convert.ToHexStringLower(hash);
        }

        var contentHash = SHA256.HashData(Encoding.UTF8.GetBytes(message.Content));
        return "m:c:" + Convert.ToHexStringLower(contentHash);
    }
}
