namespace Comprexy.Application.Models;

/// <summary>
/// Resolved connection details for an OpenAI-compatible endpoint (either the main upstream
/// provider or the, possibly distinct, compression model provider).
/// </summary>
public sealed record ProviderEndpoint(string BaseUrl, string? ApiKey, string Model, int TimeoutSeconds)
{
    /// <summary>
    /// Whether two endpoints can share prefix KV cache (same host + model). ApiKey and timeout
    /// are ignored.
    /// </summary>
    public bool SharesKvCacheWith(ProviderEndpoint other) =>
        string.Equals(NormalizeBaseUrl(BaseUrl), NormalizeBaseUrl(other.BaseUrl), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Model, other.Model, StringComparison.Ordinal);

    private static string NormalizeBaseUrl(string baseUrl) => baseUrl.Trim().TrimEnd('/');
}
