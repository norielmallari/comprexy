using System.Net.Http.Headers;
using Comprexy.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Comprexy.Infrastructure.Providers;

/// <summary>
/// Reverse-proxies non-chat <c>/v1</c> requests to the configured OpenAI-compatible upstream.
/// </summary>
public sealed class UpstreamPassthroughProxy : IUpstreamPassthroughProxy
{
    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Connection",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade",
        "Host",
        "Content-Length"
    };

    private readonly HttpClient _httpClient;
    private readonly ProviderEndpointResolver _endpointResolver;
    private readonly ILogger<UpstreamPassthroughProxy> _logger;

    public UpstreamPassthroughProxy(
        HttpClient httpClient,
        ProviderEndpointResolver endpointResolver,
        ILogger<UpstreamPassthroughProxy> logger)
    {
        _httpClient = httpClient;
        _endpointResolver = endpointResolver;
        _logger = logger;
    }

    public async Task ForwardAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var endpoint = _endpointResolver.ResolveUpstream();
        var target = UpstreamPassthroughUrlBuilder.Build(
            endpoint.BaseUrl,
            context.Request.Path.Value ?? "/",
            context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null);

        using var upstreamRequest = new HttpRequestMessage(
            new HttpMethod(context.Request.Method),
            target);

        if (HasRequestBody(context.Request.Method))
        {
            upstreamRequest.Content = new StreamContent(context.Request.Body);
            if (context.Request.ContentType is { Length: > 0 } contentType)
            {
                upstreamRequest.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            }
        }

        foreach (var header in context.Request.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key))
            {
                continue;
            }

            if (!upstreamRequest.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) &&
                upstreamRequest.Content is not null)
            {
                upstreamRequest.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        if (!string.IsNullOrWhiteSpace(endpoint.ApiKey))
        {
            upstreamRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.ApiKey);
        }

        _logger.LogDebug(
            "Passthrough {Method} {Path} → {Target}",
            context.Request.Method,
            context.Request.Path,
            target);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, endpoint.TimeoutSeconds)));

        using var upstreamResponse = await _httpClient.SendAsync(
            upstreamRequest,
            HttpCompletionOption.ResponseHeadersRead,
            timeoutCts.Token);

        context.Response.StatusCode = (int)upstreamResponse.StatusCode;

        foreach (var header in upstreamResponse.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key))
            {
                continue;
            }

            context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in upstreamResponse.Content.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key))
            {
                continue;
            }

            context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        context.Response.Headers.Remove("transfer-encoding");

        await upstreamResponse.Content.CopyToAsync(context.Response.Body, cancellationToken);
    }

    private static bool HasRequestBody(string method) =>
        !HttpMethods.IsGet(method) &&
        !HttpMethods.IsHead(method) &&
        !HttpMethods.IsDelete(method) &&
        !HttpMethods.IsTrace(method) &&
        !HttpMethods.IsOptions(method) &&
        !string.Equals(method, "CONNECT", StringComparison.OrdinalIgnoreCase);
}
