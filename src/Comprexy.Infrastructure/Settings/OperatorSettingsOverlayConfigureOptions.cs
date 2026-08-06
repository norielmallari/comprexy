using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Comprexy.Infrastructure.Settings;

/// <summary>
/// Applies SQLite operator overlay onto allowlisted options after Bind. Env/cmdline keys win
/// when present in higher-priority configuration providers.
/// </summary>
public sealed class OperatorSettingsOverlayConfigureOptions<TOptions> : IConfigureOptions<TOptions>
    where TOptions : class
{
    private readonly IOperatorSettingsOverlay _overlay;
    private readonly IConfiguration _configuration;

    public OperatorSettingsOverlayConfigureOptions(
        IOperatorSettingsOverlay overlay,
        IConfiguration configuration)
    {
        _overlay = overlay;
        _configuration = configuration;
    }

    public void Configure(TOptions options)
    {
        OperatorMutableSettingsDto dto;
        try
        {
            dto = OperatorMutableSettingsJson.Parse(_overlay.SettingsJson);
        }
        catch
        {
            return;
        }

        bool IsHigher(string key) => ConfigurationPriority.IsEnvOrCommandLine(_configuration, key);

        switch (options)
        {
            case ProxyOptions proxy:
                OperatorMutableSettingsJson.ApplyOverlayToProxy(proxy, dto, IsHigher);
                break;
            case ContextPolicyOptions contextPolicy:
                OperatorMutableSettingsJson.ApplyOverlayToContextPolicy(contextPolicy, dto, IsHigher);
                break;
            case CacheAlignmentOptions cacheAlignment:
                OperatorMutableSettingsJson.ApplyOverlayToCacheAlignment(cacheAlignment, dto, IsHigher);
                break;
            case MetricsOptions metrics:
                OperatorMutableSettingsJson.ApplyOverlayToMetrics(metrics, dto, IsHigher);
                break;
            case ToolSchemaOptions toolSchema:
                OperatorMutableSettingsJson.ApplyOverlayToToolSchema(toolSchema, dto, IsHigher);
                break;
        }
    }
}

internal static class ConfigurationPriority
{
    public static bool IsEnvOrCommandLine(IConfiguration configuration, string key)
    {
        if (configuration is not IConfigurationRoot root)
        {
            return false;
        }

        // Last provider that supplies the key wins in ConfigurationRoot.
        for (var i = root.Providers.Count() - 1; i >= 0; i--)
        {
            var provider = root.Providers.ElementAt(i);
            if (!provider.TryGet(key, out _))
            {
                // Also try double-underscore env form
                var envKey = key.Replace(':', '_');
                if (!provider.TryGet(envKey, out _) &&
                    !provider.TryGet(envKey.ToUpperInvariant(), out _))
                {
                    continue;
                }
            }

            var name = provider.GetType().Name;
            return name.Contains("Environment", StringComparison.OrdinalIgnoreCase)
                || name.Contains("CommandLine", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
