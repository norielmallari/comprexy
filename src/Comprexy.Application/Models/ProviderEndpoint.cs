using System.Text.Json;

namespace Comprexy.Application.Models;

/// <summary>
/// Resolved connection details for an OpenAI-compatible endpoint (either the main upstream
/// provider or the, possibly distinct, compression model provider).
/// </summary>
public sealed record ProviderEndpoint(string BaseUrl, string? ApiKey, string? Model, int TimeoutSeconds)
{
    /// <summary>
    /// Whether two endpoints can share prefix KV cache (same host + model). ApiKey and timeout
    /// are ignored. A null/empty model does not match a concrete model id.
    /// </summary>
    public bool SharesKvCacheWith(ProviderEndpoint other) =>
        string.Equals(NormalizeBaseUrl(BaseUrl), NormalizeBaseUrl(other.BaseUrl), StringComparison.OrdinalIgnoreCase) &&
        HasConfiguredModel &&
        other.HasConfiguredModel &&
        string.Equals(Model, other.Model, StringComparison.Ordinal);

    /// <summary>True when <see cref="Model"/> is non-empty and should override the client request.</summary>
    public bool HasConfiguredModel => !string.IsNullOrWhiteSpace(Model);

    /// <summary>
    /// When this endpoint has no configured model, returns a copy with <paramref name="preferredModel"/>.
    /// </summary>
    public ProviderEndpoint WithPreferredModel(string? preferredModel)
    {
        if (HasConfiguredModel || string.IsNullOrWhiteSpace(preferredModel))
        {
            return this;
        }

        return this with { Model = preferredModel };
    }

    /// <summary>
    /// Configured model when set; otherwise the client's <c>model</c> from
    /// <paramref name="originalClientRequest"/>; otherwise empty.
    /// </summary>
    public string ResolveOutboundModel(JsonElement? originalClientRequest)
    {
        if (HasConfiguredModel)
        {
            return Model!;
        }

        if (originalClientRequest is { ValueKind: JsonValueKind.Object } raw &&
            raw.TryGetProperty("model", out var model) &&
            model.ValueKind == JsonValueKind.String)
        {
            return model.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string NormalizeBaseUrl(string baseUrl) => baseUrl.Trim().TrimEnd('/');
}
