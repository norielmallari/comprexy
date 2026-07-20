using System.Text.Json;
using Comprexy.Application.Models;

namespace Comprexy.Application.Abstractions;

/// <summary>
/// Approximates the number of tokens a prompt would consume against the target model.
/// </summary>
public interface ITokenEstimator
{
    int CountTokens(string text);

    /// <summary>
    /// Counts message tokens, preferring each message's raw wire JSON when present
    /// (includes tool_calls, multimodal parts, etc.) rather than extracted text only.
    /// </summary>
    int CountTokens(IEnumerable<ChatMessage> messages);

    /// <summary>
    /// Counts messages plus request-level prompt payloads such as <c>tools</c>, <c>functions</c>,
    /// <c>tool_choice</c>, and <c>response_format</c> from <paramref name="requestRoot"/>.
    /// </summary>
    int CountPromptTokens(IEnumerable<ChatMessage> messages, JsonElement? requestRoot = null);
}
