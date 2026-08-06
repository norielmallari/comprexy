using Comprexy.Application.Models;

namespace Comprexy.Application.Services.Settings;

/// <summary>
/// Unset accessor for non-chat hosts. <see cref="IsSet"/> is false until <see cref="Set"/>
/// (compat constructors may Set during prepare tests).
/// </summary>
public sealed class UnsetEffectiveSettingsAccessor : IEffectiveSettingsAccessor
{
    public static UnsetEffectiveSettingsAccessor Instance { get; } = new();

    private EffectiveSettingsV1? _current;

    public bool IsSet => _current is not null;

    public EffectiveSettingsV1 Current =>
        _current ?? throw new InvalidOperationException("Effective settings accessor is unset.");

    public void Set(EffectiveSettingsV1 settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _current = settings;
    }
}
