namespace Comprexy.Application.Configuration;

/// <summary>
/// Configuration for the upstream OpenAI-compatible LLM provider that Comprexy proxies to.
/// </summary>
public class ProviderOptions
{
    public const string SectionName = "Provider";

    /// <summary>Only supported provider kind in v1.</summary>
    public const string OpenAiCompatibleType = "OpenAICompatible";

    /// <summary>Provider kind. Only <see cref="OpenAiCompatibleType"/> is supported in v1.</summary>
    public string Type { get; set; } = OpenAiCompatibleType;

    public string BaseUrl { get; set; } = "http://localhost:11434/v1";

    /// <summary>
    /// Optional Bearer token for the upstream provider. When null or empty, no
    /// <c>Authorization</c> header is sent (common for local OpenAI-compatible servers).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Upstream model id. When null or whitespace, chat and compression use the client's
    /// <c>model</c> from the request that triggered the work. Set <c>Compression:Model</c> to
    /// force a dedicated compression model.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>Request timeout, in seconds, for upstream chat completion calls.</summary>
    public int TimeoutSeconds { get; set; } = 120;
}
