namespace Comprexy.Domain.Enums;

/// <summary>
/// Proxy optimization intensity. Distinct from <c>Proxy:PassThrough</c>, which always wins when true.
/// </summary>
public enum OptimizationMode
{
    /// <summary>Normal prepare: rules, Virtual Tools, budget, wrap-up per sticky/live knobs.</summary>
    Full = 0,

    /// <summary>
    /// PassThrough-like wire (client body unchanged) with prompt mutations skipped and optional
    /// metrics when <c>Metrics:Enabled</c>. BaseSystem may be captured for observability.
    /// </summary>
    MonitorOnly = 1
}
