namespace Comprexy.Application.Services;

/// <summary>
/// Builds the upstream URL for OpenAI-compatible passthrough of non-chat routes.
/// </summary>
public static class UpstreamPassthroughUrlBuilder
{
    /// <summary>
    /// Maps an incoming Comprexy path (typically under <c>/v1</c>) onto the configured provider base URL.
    /// When the provider base already ends with <c>/v1</c>, only the suffix after <c>/v1</c> is appended.
    /// </summary>
    public static Uri Build(string providerBaseUrl, string requestPath, string? queryString)
    {
        if (string.IsNullOrWhiteSpace(providerBaseUrl))
        {
            throw new ArgumentException("Provider base URL is required.", nameof(providerBaseUrl));
        }

        var path = string.IsNullOrWhiteSpace(requestPath) ? "/" : requestPath;
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        var baseUrl = providerBaseUrl.TrimEnd('/');
        var query = string.IsNullOrEmpty(queryString) ? string.Empty : queryString;

        string target;
        if (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) &&
            path.StartsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = path.Length == 3 ? string.Empty : path[3..];
            target = baseUrl + suffix + query;
        }
        else
        {
            target = baseUrl + path + query;
        }

        return new Uri(target, UriKind.Absolute);
    }
}
