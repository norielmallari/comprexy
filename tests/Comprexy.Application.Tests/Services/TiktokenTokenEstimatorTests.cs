using System.Text.Json;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Domain.Enums;
using Comprexy.Infrastructure.Tokenization;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Tests.Services;

public class TiktokenTokenEstimatorTests
{
    private static TiktokenTokenEstimator CreateEstimator() =>
        new(
            Options.Create(new ContextPolicyOptions { TokenizerEncoding = "cl100k_base" }),
            new PassthroughTokenEstimateCache());

    [Fact]
    public void CountTokens_Messages_PrefersRawWireJsonOverExtractedText()
    {
        var estimator = CreateEstimator();
        using var document = JsonDocument.Parse(
            """{"role":"assistant","content":null,"tool_calls":[{"id":"1","type":"function","function":{"name":"lookup","arguments":"{\"q\":\"abc\"}"}}]}""");

        var textOnly = new ChatMessage(MessageRole.Assistant, string.Empty);
        var withWire = new ChatMessage(MessageRole.Assistant, string.Empty, document.RootElement.Clone());

        var textTokens = estimator.CountTokens([textOnly]);
        var wireTokens = estimator.CountTokens([withWire]);

        Assert.True(wireTokens > textTokens);
    }

    [Fact]
    public void CountPromptTokens_IncludesToolsPayload()
    {
        var estimator = CreateEstimator();
        using var request = JsonDocument.Parse(
            """
            {
              "messages": [{"role":"user","content":"hi"}],
              "tools": [
                {
                  "type": "function",
                  "function": {
                    "name": "lookup",
                    "description": "Look things up in a very large catalog of items and return matches",
                    "parameters": {
                      "type": "object",
                      "properties": {
                        "query": { "type": "string", "description": "search query" }
                      }
                    }
                  }
                }
              ]
            }
            """);

        var message = new ChatMessage(MessageRole.User, "hi");
        var withoutTools = estimator.CountPromptTokens([message]);
        var withTools = estimator.CountPromptTokens([message], request.RootElement);

        Assert.True(withTools > withoutTools);
    }

    [Fact]
    public void CountTokens_UsesCache_OnRepeatedMessage()
    {
        var cache = new CountingTokenEstimateCache();
        var estimator = new TiktokenTokenEstimator(
            Options.Create(new ContextPolicyOptions { TokenizerEncoding = "cl100k_base" }),
            cache);

        var message = new ChatMessage(MessageRole.User, "cache-me-please");
        var first = estimator.CountTokens([message]);
        var second = estimator.CountTokens([message]);

        Assert.Equal(first, second);
        Assert.Equal(1, cache.ComputeCount);
        Assert.Equal(2, cache.LookupCount);
    }

    private sealed class PassthroughTokenEstimateCache : ITokenEstimateCache
    {
        public int GetOrCompute(string key, Func<int> compute, CancellationToken cancellationToken = default) =>
            compute();
    }

    private sealed class CountingTokenEstimateCache : ITokenEstimateCache
    {
        private readonly Dictionary<string, int> _store = new();

        public int ComputeCount { get; private set; }
        public int LookupCount { get; private set; }

        public int GetOrCompute(string key, Func<int> compute, CancellationToken cancellationToken = default)
        {
            LookupCount++;
            if (_store.TryGetValue(key, out var cached))
            {
                return cached;
            }

            ComputeCount++;
            var value = compute();
            _store[key] = value;
            return value;
        }
    }
}
