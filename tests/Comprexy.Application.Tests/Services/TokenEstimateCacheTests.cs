using System.Text.Json;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Tests.Services;

public class TokenEstimateCacheTests
{
    private readonly TokenEstimateCacheOptions _options = new()
    {
        AbsoluteExpiration = TimeSpan.FromMinutes(15),
        SizeLimit = 10_000
    };

    private TokenEstimateCache CreateService(IMemoryCache? cache = null, TokenEstimateCacheOptions? options = null) =>
        cache is null
            ? new TokenEstimateCache(Options.Create(options ?? _options))
            : new TokenEstimateCache(cache, Options.Create(options ?? _options), ownsCache: false);

    [Fact]
    public void GetOrCompute_CacheHit_ReturnsCachedValueWithoutCallingCompute()
    {
        using var service = CreateService();
        string key = TokenEstimateCache.ComputeStringKey("hello");
        int computeCallCount = 0;

        var result1 = service.GetOrCompute(key, () =>
        {
            computeCallCount++;
            return 42;
        });
        var result2 = service.GetOrCompute(key, () =>
        {
            computeCallCount++;
            return 42;
        });

        Assert.Equal(42, result1);
        Assert.Equal(42, result2);
        Assert.Equal(1, computeCallCount);
    }

    [Fact]
    public void GetOrCompute_CacheMiss_CallsComputeAndStoresResult()
    {
        using var service = CreateService();
        string key = TokenEstimateCache.ComputeStringKey("unique-key-miss");
        int computeCallCount = 0;

        var result = service.GetOrCompute(key, () =>
        {
            computeCallCount++;
            return 100;
        });

        Assert.Equal(100, result);
        Assert.Equal(1, computeCallCount);
        Assert.Equal(100, service.GetOrCompute(key, () =>
        {
            computeCallCount++;
            return 100;
        }));
        Assert.Equal(1, computeCallCount);
    }

    [Fact]
    public void GetOrCompute_TtlExpiration_CausesReComputation()
    {
        var clock = new ManualSystemClock(DateTimeOffset.UtcNow);
        var memory = new MemoryCache(new MemoryCacheOptions { Clock = clock, SizeLimit = 100 });
        var options = new TokenEstimateCacheOptions
        {
            AbsoluteExpiration = TimeSpan.FromMinutes(15),
            SizeLimit = 100
        };
        using var service = CreateService(memory, options);

        string key = TokenEstimateCache.ComputeStringKey("ttl-test");
        int computeCallCount = 0;

        Assert.Equal(1, service.GetOrCompute(key, () => ++computeCallCount));

        clock.UtcNow += TimeSpan.FromMinutes(16);

        Assert.Equal(2, service.GetOrCompute(key, () => ++computeCallCount));
        Assert.Equal(2, computeCallCount);
    }

    [Fact]
    public async Task GetOrCompute_ConcurrentCallsWithSameKey_InvokesComputeOnlyOnce()
    {
        using var service = CreateService();
        string key = TokenEstimateCache.ComputeStringKey("stampede-test");
        int computeCallCount = 0;

        Func<int> compute = () =>
        {
            Interlocked.Increment(ref computeCallCount);
            Thread.Sleep(50);
            return 999;
        };

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => service.GetOrCompute(key, compute)))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal(999, r));
        Assert.Equal(1, computeCallCount);
    }

    [Fact]
    public void GetOrCompute_SizeLimit_EvictsEntriesWhenExceeded()
    {
        var options = new TokenEstimateCacheOptions
        {
            AbsoluteExpiration = TimeSpan.FromMinutes(15),
            SizeLimit = 2
        };
        using var service = new TokenEstimateCache(Options.Create(options));

        var key1 = TokenEstimateCache.ComputeStringKey("a");
        var key2 = TokenEstimateCache.ComputeStringKey("b");
        var key3 = TokenEstimateCache.ComputeStringKey("c");

        service.GetOrCompute(key1, () => 1);
        service.GetOrCompute(key2, () => 2);
        service.GetOrCompute(key3, () => 3);

        // With SizeLimit=2 and Size=1 per entry, at least one of the first two should be gone
        // after inserting the third (MemoryCache compact on over-capacity).
        int hits = 0;
        if (service.GetOrCompute(key1, () => { hits++; return 1; }) == 1 && hits == 0)
        {
            // still cached
        }

        hits = 0;
        service.GetOrCompute(key1, () => { hits++; return 11; });
        service.GetOrCompute(key2, () => { hits++; return 22; });
        service.GetOrCompute(key3, () => { hits++; return 33; });

        // After three inserts into a size-2 cache, re-fetching all three must recompute at least once.
        Assert.True(hits >= 1, $"Expected eviction-driven recompute, hits={hits}");
    }

    [Fact]
    public void GetOrCompute_Canceled_ThrowsBeforeCompute()
    {
        using var service = CreateService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            service.GetOrCompute("k", () => 1, cts.Token));
    }

    [Fact]
    public void ComputeStringKey_IsNamespacedAndStable()
    {
        var key = TokenEstimateCache.ComputeStringKey("test content");
        Assert.StartsWith("s:", key);
        Assert.Equal(key, TokenEstimateCache.ComputeStringKey("test content"));
        Assert.NotEqual(
            TokenEstimateCache.ComputeStringKey("hello"),
            TokenEstimateCache.ComputeStringKey("world"));
    }

    [Fact]
    public void ComputeMessageKey_ObjectWire_UsesWireNamespace()
    {
        using var document = JsonDocument.Parse("""{"role":"user","content":"hi"}""");
        var message = new ChatMessage(MessageRole.User, "ignored", document.RootElement.Clone());
        var key = TokenEstimateCache.ComputeMessageKey(message);
        Assert.StartsWith("m:w:", key);
    }

    [Fact]
    public void ComputeMessageKey_NonObjectWire_UsesContentNamespace()
    {
        using var document = JsonDocument.Parse("\"just a string\"");
        var message = new ChatMessage(MessageRole.User, "hello", document.RootElement.Clone());
        var key = TokenEstimateCache.ComputeMessageKey(message);
        Assert.StartsWith("m:c:", key);
        Assert.Equal(TokenEstimateCache.ComputeMessageKey(new ChatMessage(MessageRole.User, "hello")), key);
    }

    [Fact]
    public void ComputeKeys_StringAndMessage_DoNotCollide()
    {
        var content = "same-body";
        var stringKey = TokenEstimateCache.ComputeStringKey(content);
        var messageKey = TokenEstimateCache.ComputeMessageKey(new ChatMessage(MessageRole.User, content));
        Assert.NotEqual(stringKey, messageKey);
    }

    [Fact]
    public void GetOrCompute_AfterDispose_ThrowsObjectDisposedException()
    {
        var service = new TokenEstimateCache(Options.Create(_options));
        service.Dispose();
        Assert.Throws<ObjectDisposedException>(() => service.GetOrCompute("key", () => 1));
    }

    [Fact]
    public void Dispose_DoesNotDisposeInjectedSharedCache()
    {
        var memory = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var service = new TokenEstimateCache(memory, Options.Create(_options), ownsCache: false);
        service.GetOrCompute(TokenEstimateCache.ComputeStringKey("x"), () => 7);
        service.Dispose();

        // Shared cache still usable after TokenEstimateCache dispose.
        Assert.Equal(7, memory.Get<int>(TokenEstimateCache.ComputeStringKey("x")));
        memory.Dispose();
    }

    private sealed class ManualSystemClock(DateTimeOffset utcNow) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
