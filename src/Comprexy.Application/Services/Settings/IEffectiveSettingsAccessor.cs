using Comprexy.Application.Models;

namespace Comprexy.Application.Services.Settings;

/// <summary>
/// Request-scoped holder for resolved sticky/live effective settings. Set once at the start of
/// prepare (before Enrich/rewind/rules/VT). Cleared when the request scope ends.
/// </summary>
public interface IEffectiveSettingsAccessor
{
    bool IsSet { get; }

    EffectiveSettingsV1 Current { get; }

    void Set(EffectiveSettingsV1 settings);
}
