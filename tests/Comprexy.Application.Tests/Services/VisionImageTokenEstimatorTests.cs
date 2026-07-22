using System.Text.Json;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services;
using Comprexy.Domain.Enums;
using Comprexy.Infrastructure.Tokenization;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Tests.Services;

public class VisionImageTokenEstimatorTests
{
    // 1x1 PNG
    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    // IHDR only prefix for 1024x1536 (same header as production screenshot)
    private const string Png1024x1536Prefix =
        "iVBORw0KGgoAAAANSUhEUgAABAAAAAYACAIAAABn4K39AAAA";

    [Fact]
    public void TryGetDimensions_ReadsPngIhdr()
    {
        var url = $"data:image/png;base64,{TinyPngBase64}";

        Assert.True(VisionImageTokenEstimator.TryGetDimensions(url, out var width, out var height));
        Assert.Equal(1, width);
        Assert.Equal(1, height);
    }

    [Fact]
    public void Estimate_LowDetail_IsFixed()
    {
        var url = $"data:image/png;base64,{TinyPngBase64}";

        Assert.Equal(
            VisionImageTokenEstimator.LowDetailTokens,
            VisionImageTokenEstimator.Estimate(url, "low"));
    }

    [Fact]
    public void Estimate_HighDetail_UsesTileFormula()
    {
        var url = $"data:image/png;base64,{Png1024x1536Prefix}";

        Assert.True(VisionImageTokenEstimator.TryGetDimensions(url, out var w, out var h));
        Assert.Equal(1024, w);
        Assert.Equal(1536, h);
        Assert.Equal(1105, VisionImageTokenEstimator.Estimate(url, "high"));
    }

    [Fact]
    public void RedactImageUrlForText_HidesBase64Payload()
    {
        var url = $"data:image/png;base64,{TinyPngBase64}";
        var redacted = VisionImageTokenEstimator.RedactImageUrlForText(url);

        Assert.DoesNotContain(TinyPngBase64, redacted);
        Assert.Contains("data:png;base64", redacted);
    }
}

public class TiktokenTokenEstimatorImageTests
{
    private static TiktokenTokenEstimator CreateEstimator() =>
        new(
            Options.Create(new ContextPolicyOptions { TokenizerEncoding = "cl100k_base" }),
            new PassthroughTokenEstimateCache());

    // Large fake base64 body (not a real image) — must not dominate token count via BPE.
    private static string HugeFakeDataUrl(int base64Chars) =>
        "data:image/png;base64," + new string('A', base64Chars);

    [Fact]
    public void CountTokens_WireMessage_DoesNotBpeTokenizeImageBase64()
    {
        var estimator = CreateEstimator();
        var hugeUrl = HugeFakeDataUrl(200_000);
        using var document = JsonDocument.Parse(
            $$"""
            {
              "role": "user",
              "content": [
                { "type": "text", "text": "look" },
                { "type": "image_url", "image_url": { "url": "{{hugeUrl}}", "detail": "low" } }
              ]
            }
            """);

        var message = new ChatMessage(MessageRole.User, "look", document.RootElement.Clone());
        var tokens = estimator.CountTokens([message]);

        // Low-detail image is 85 tokens; text/json overhead is small. Base64 BPE would be >> 10k.
        Assert.InRange(tokens, 80, 500);
    }

    [Fact]
    public void CountTokens_WireMessage_UsesPngDimensionsForHighDetail()
    {
        var estimator = CreateEstimator();
        // Minimal valid-looking PNG header for 1024x1536 plus padding so it's a data URL
        var url = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAABAAAAAYACAIAAABn4K39" + new string('A', 64);
        using var document = JsonDocument.Parse(
            $$"""
            {
              "role": "user",
              "content": [
                { "type": "text", "text": "x" },
                { "type": "image_url", "image_url": { "url": "{{url}}" } }
              ]
            }
            """);

        var message = new ChatMessage(MessageRole.User, "x", document.RootElement.Clone());
        var tokens = estimator.CountTokens([message]);

        // 1105 image + small wire/text overhead
        Assert.InRange(tokens, 1100, 1300);
    }

    private sealed class PassthroughTokenEstimateCache : ITokenEstimateCache
    {
        public int GetOrCompute(string key, Func<int> compute, CancellationToken cancellationToken = default) =>
            compute();
    }
}
