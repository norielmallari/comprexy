using Comprexy.Api.Contracts;
using Comprexy.Application.Configuration;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Comprexy.Api.Middleware;

/// <summary>
/// When <see cref="AuthOptions.RequiredApiKey"/> is configured, requires clients to send a
/// matching API key on <c>/v1/*</c> via <c>Authorization: Bearer &lt;key&gt;</c>
/// (scheme case-insensitive; surrounding whitespace allowed) or <c>X-Api-Key: &lt;key&gt;</c>.
/// <c>/health</c> and other non-<c>/v1</c> paths are exempt so probes can stay unauthenticated.
/// When the key is unset, any (or no) credential header is accepted.
/// </summary>
public class ApiKeyAuthMiddleware(RequestDelegate next, IOptions<AuthOptions> authOptions)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/v1"))
        {
            await next(context);
            return;
        }

        var requiredApiKey = authOptions.Value.RequiredApiKey;
        if (string.IsNullOrWhiteSpace(requiredApiKey))
        {
            await next(context);
            return;
        }

        if (!ApiKeyCredential.Matches(context.Request, requiredApiKey))
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

        await next(context);
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
