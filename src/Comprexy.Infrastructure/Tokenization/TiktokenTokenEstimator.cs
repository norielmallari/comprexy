using System.Text.Json;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services;
using Microsoft.Extensions.Options;
using Microsoft.ML.Tokenizers;

namespace Comprexy.Infrastructure.Tokenization;

/// <summary>
/// Approximates token counts using a Tiktoken BPE encoding. Counts full wire message JSON when
/// available, plus request-level prompt fields (tools, etc.), not just extracted text.
/// Individual string and per-message counts are memoized via <see cref="ITokenEstimateCache"/>.
/// </summary>
public class TiktokenTokenEstimator : ITokenEstimator
{
    /// <summary>Approximate per-message formatting overhead when only plain text is available.</summary>
    private const int PerMessageOverheadTokens = 4;

    private static readonly string[] PromptSideProperties =
    [
        "tools",
        "functions",
        "tool_choice",
        "response_format"
    ];

    private readonly Tokenizer _tokenizer;
    private readonly ITokenEstimateCache _tokenEstimateCache;

    public TiktokenTokenEstimator(
        IOptions<ContextPolicyOptions> policy,
        ITokenEstimateCache tokenEstimateCache)
    {
        _tokenizer = TiktokenTokenizer.CreateForEncoding(policy.Value.TokenizerEncoding);
        _tokenEstimateCache = tokenEstimateCache;
    }

    public int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var key = TokenEstimateCache.ComputeStringKey(text);
        return _tokenEstimateCache.GetOrCompute(key, () => _tokenizer.CountTokens(text));
    }

    public int CountTokens(IEnumerable<ChatMessage> messages) =>
        messages.Sum(CountMessageTokens);

    public int CountPromptTokens(IEnumerable<ChatMessage> messages, JsonElement? requestRoot = null)
    {
        var total = CountTokens(messages);
        if (requestRoot is not { ValueKind: JsonValueKind.Object } root)
        {
            return total;
        }

        foreach (var propertyName in PromptSideProperties)
        {
            if (root.TryGetProperty(propertyName, out var value) &&
                value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                total += CountTokens(value.GetRawText());
            }
        }

        return total;
    }

    private int CountMessageTokens(ChatMessage message)
    {
        var key = TokenEstimateCache.ComputeMessageKey(message);
        return _tokenEstimateCache.GetOrCompute(key, () => CountMessageTokensUncached(message));
    }

    private int CountMessageTokensUncached(ChatMessage message)
    {
        if (message.RawWireMessage is { ValueKind: JsonValueKind.Object } raw)
        {
            // Full OpenAI message object: role, content (string or parts), tool_calls, etc.
            var wire = raw.GetRawText();
            return string.IsNullOrEmpty(wire) ? 0 : _tokenizer.CountTokens(wire);
        }

        var contentTokens = string.IsNullOrEmpty(message.Content)
            ? 0
            : _tokenizer.CountTokens(message.Content);
        return contentTokens + PerMessageOverheadTokens;
    }
}
