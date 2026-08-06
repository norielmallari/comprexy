namespace Comprexy.Application.Configuration;

/// <summary>
/// Optional shared-secret check for clients calling Comprexy <c>/v1/*</c> and control-api <c>/mcp</c>.
/// When the resolved expected key for a path is null or empty, any (or no) credential is accepted.
/// <c>/health</c> is never gated by these keys.
/// </summary>
public class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// Shared secret for proxy <c>/v1/*</c> and control-api <c>/mcp</c>.
    /// Also used as the <c>/v1</c> fallback on control-api when
    /// <see cref="ProtectV1WithDashboardKey"/> is true and <see cref="DashboardApiKey"/> is empty.
    /// Send <c>Authorization: Bearer {value}</c> (scheme case-insensitive) or <c>X-Api-Key: {value}</c>.
    /// </summary>
    public string? RequiredApiKey { get; set; }

    /// <summary>
    /// Dashboard / control-api <c>/v1</c> unlock key when <see cref="ProtectV1WithDashboardKey"/> is true.
    /// Never accepted as a substitute for <see cref="RequiredApiKey"/> on <c>/mcp</c>.
    /// </summary>
    public string? DashboardApiKey { get; set; }

    /// <summary>
    /// When true (stock control-api), <c>/v1</c> resolves the expected key as:
    /// non-empty <see cref="DashboardApiKey"/>, else non-empty <see cref="RequiredApiKey"/> fallback,
    /// else open. When false (stock proxy), <c>/v1</c> uses <see cref="RequiredApiKey"/> only.
    /// </summary>
    public bool ProtectV1WithDashboardKey { get; set; }
}
