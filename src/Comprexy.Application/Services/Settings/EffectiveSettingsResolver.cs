using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services.Settings;

/// <summary>
/// Resolves sticky snapshot JSON or live allowlisted capture when null (legacy / unbound).
/// </summary>
public static class EffectiveSettingsResolver
{
    public static EffectiveSettingsV1 Resolve(
        string? effectiveSettingsJson,
        IOptionsMonitor<ProxyOptions> proxy,
        IOptionsMonitor<ContextPolicyOptions> contextPolicy,
        IOptionsMonitor<CacheAlignmentOptions> cacheAlignment,
        IOptionsMonitor<MetricsOptions> metrics,
        IOptionsMonitor<ToolSchemaOptions> toolSchema)
    {
        if (!string.IsNullOrWhiteSpace(effectiveSettingsJson))
        {
            return EffectiveSettingsSerializer.Deserialize(effectiveSettingsJson);
        }

        return EffectiveSettingsSerializer.CaptureFrom(
            proxy,
            contextPolicy,
            cacheAlignment,
            metrics,
            toolSchema);
    }
}
