using Comprexy.Application.Models;

namespace Comprexy.Application.Services.Settings;

public sealed class EffectiveSettingsAccessor : IEffectiveSettingsAccessor
{
    private EffectiveSettingsV1? _current;

    public bool IsSet => _current is not null;

    public EffectiveSettingsV1 Current =>
        _current ?? throw new InvalidOperationException(
            "Effective settings have not been set for this request scope.");

    public void Set(EffectiveSettingsV1 settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _current = settings;
    }
}
