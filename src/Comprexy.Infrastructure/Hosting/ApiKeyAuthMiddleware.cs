using Comprexy.Application.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Comprexy.Infrastructure.Hosting;

/// <summary>
/// Path-split API-key gate for <c>/v1/*</c> and <c>/mcp</c>.
/// <c>/mcp</c> always uses <see cref="AuthOptions.RequiredApiKey"/> (empty → open).
/// <c>/v1</c> uses dashboard-key resolution when <see cref="AuthOptions.ProtectV1WithDashboardKey"/>
/// is true; otherwise <see cref="AuthOptions.RequiredApiKey"/> only (proxy).
/// <c>/health</c> and other unprotected paths are exempt.
/// </summary>
public class ApiKeyAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IOptions<AuthOptions> _authOptions;
    private readonly ILogger<ApiKeyAuthMiddleware> _logger;
    private int _v1RequiredFallbackLogged;

    public ApiKeyAuthMiddleware(RequestDelegate next, IOptions<AuthOptions> authOptions)
        : this(next, authOptions, NullLogger<ApiKeyAuthMiddleware>.Instance)
    {
    }

    public ApiKeyAuthMiddleware(
        RequestDelegate next,
        IOptions<AuthOptions> authOptions,
        ILogger<ApiKeyAuthMiddleware> logger)
    {
        _next = next;
        _authOptions = authOptions;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var auth = _authOptions.Value;
        if (!TryResolveExpectedKey(context.Request.Path, auth, out var expectedKey))
        {
            await _next(context);
            return;
        }

        if (IsV1RequiredApiKeyFallback(context.Request.Path, auth))
        {
            LogV1RequiredFallbackOnce();
        }

        if (string.IsNullOrWhiteSpace(expectedKey))
        {
            await _next(context);
            return;
        }

        if (!ApiKeyCredential.Matches(context.Request, expectedKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new ErrorResponseDto
            {
                Error = new ErrorDetailDto
                {
                    Message = "Invalid or missing API key.",
                    Type = "authentication_error"
                }
            });
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Returns false when the path is unprotected (caller should pass through).
    /// When true, <paramref name="expectedKey"/> is the key to match; null/whitespace means open.
    /// </summary>
    internal static bool TryResolveExpectedKey(PathString path, AuthOptions auth, out string? expectedKey)
    {
        expectedKey = null;

        if (path.StartsWithSegments("/mcp"))
        {
            expectedKey = auth.RequiredApiKey;
            return true;
        }

        if (!path.StartsWithSegments("/v1"))
        {
            return false;
        }

        if (!auth.ProtectV1WithDashboardKey)
        {
            expectedKey = auth.RequiredApiKey;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(auth.DashboardApiKey))
        {
            expectedKey = auth.DashboardApiKey;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(auth.RequiredApiKey))
        {
            expectedKey = auth.RequiredApiKey;
            return true;
        }

        expectedKey = null;
        return true;
    }

    private static bool IsV1RequiredApiKeyFallback(PathString path, AuthOptions auth) =>
        path.StartsWithSegments("/v1")
        && auth.ProtectV1WithDashboardKey
        && string.IsNullOrWhiteSpace(auth.DashboardApiKey)
        && !string.IsNullOrWhiteSpace(auth.RequiredApiKey);

    private void LogV1RequiredFallbackOnce()
    {
        if (Interlocked.Exchange(ref _v1RequiredFallbackLogged, 1) != 0)
        {
            return;
        }

        _logger.LogWarning(
            "Auth:ProtectV1WithDashboardKey is enabled but Auth:DashboardApiKey is empty; " +
            "/v1 is requiring Auth:RequiredApiKey as a migration fallback. " +
            "Set Auth:DashboardApiKey for dashboard REST, and keep RequiredApiKey for /mcp (and proxy).");
    }
}

/// <summary>
/// Parses client API-key credentials from common OpenAI-compatible headers.
/// </summary>
public static class ApiKeyCredential
{
    public const string ApiKeyHeaderName = "X-Api-Key";

    public static bool Matches(HttpRequest request, string requiredApiKey)
    {
        if (TryGetBearerToken(request.Headers.Authorization.ToString(), out var bearer)
            && FixedTimeEquals(bearer, requiredApiKey))
        {
            return true;
        }

        if (request.Headers.TryGetValue(ApiKeyHeaderName, out var apiKeyValues)
            && FixedTimeEquals(apiKeyValues.ToString().Trim(), requiredApiKey))
        {
            return true;
        }

        return false;
    }

    public static bool TryGetBearerToken(string? authorizationHeader, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrWhiteSpace(authorizationHeader))
        {
            return false;
        }

        var value = authorizationHeader.Trim();
        const string bearer = "Bearer";
        if (!value.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (value.Length == bearer.Length)
        {
            return false;
        }

        // Require at least one whitespace separator after the scheme (RFC 7235).
        if (!char.IsWhiteSpace(value[bearer.Length]))
        {
            return false;
        }

        token = value[(bearer.Length + 1)..].Trim();
        return token.Length > 0;
    }

    private static bool FixedTimeEquals(string provided, string expected)
    {
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
