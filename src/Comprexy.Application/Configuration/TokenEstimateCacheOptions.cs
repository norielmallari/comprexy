namespace Comprexy.Application.Configuration;

/// <summary>
/// Controls the dedicated in-memory token-estimate cache. Messages are append-only, so
/// TTL-only expiration is sufficient — there is no write-side invalidation.
/// </summary>
public class TokenEstimateCacheOptions
{
    public const string SectionName = "Comprexy:TokenEstimateCache";

    /// <summary>Default max cached estimates when <see cref="SizeLimit"/> is unset.</summary>
    public const int DefaultSizeLimit = 10_000;

    /// <summary>How long a cached estimate remains valid. Default 15 minutes.</summary>
    public TimeSpan AbsoluteExpiration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Max number of cached estimates (each entry has size 1). Defaults to
    /// <see cref="DefaultSizeLimit"/> when null.
    /// </summary>
    public int? SizeLimit { get; set; } = DefaultSizeLimit;
}
