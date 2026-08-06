using Comprexy.Application.Abstractions;

namespace Comprexy.Infrastructure.Settings;

public sealed class OperatorSettingsOverlay : IOperatorSettingsOverlay
{
    private readonly object _gate = new();
    private long _revision = -1;
    private string _settingsJson = "{}";

    public long Revision
    {
        get
        {
            lock (_gate)
            {
                return _revision;
            }
        }
    }

    public string SettingsJson
    {
        get
        {
            lock (_gate)
            {
                return _settingsJson;
            }
        }
    }

    public bool TryUpdate(long revision, string settingsJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsJson);
        lock (_gate)
        {
            if (revision < _revision)
            {
                return false;
            }

            if (revision == _revision && string.Equals(_settingsJson, settingsJson, StringComparison.Ordinal))
            {
                return false;
            }

            _revision = revision;
            _settingsJson = settingsJson;
            return true;
        }
    }
}
