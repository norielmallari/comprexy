namespace Comprexy.Application.Configuration;

/// <summary>
/// Optional shared-secret check for clients calling Comprexy <c>/v1/*</c> routes. When
/// <see cref="RequiredApiKey"/> is null or empty, any (or no) Authorization header is accepted.
/// <c>/health</c> is never gated by this key.
/// </summary>
public class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// When set, <c>/v1/*</c> requests must send <c>Authorization: Bearer {value}</c>
    /// (scheme case-insensitive) or <c>X-Api-Key: {value}</c>.
    /// </summary>
    public string? RequiredApiKey { get; set; }
}
