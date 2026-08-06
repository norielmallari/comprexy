namespace Comprexy.Domain.Entities;

/// <summary>
/// Singleton-row mutable operator settings owned by control-api (SQLite). Proxy polls revision
/// and applies allowlisted JSON via options overlay.
/// </summary>
public class OperatorSettings : EntityBase
{
    /// <summary>Optimistic concurrency / ETag revision. Bumped on each successful PUT.</summary>
    public long Revision { get; private set; }

    /// <summary>Allowlisted settings JSON (section-shaped). Secrets never stored.</summary>
    public string SettingsJson { get; private set; } = "{}";

    public DateTimeOffset UpdatedAt { get; private set; }

    private OperatorSettings()
    {
    }

    public static OperatorSettings CreateSeed(Guid id, DateTimeOffset now) =>
        new()
        {
            Id = id,
            Revision = 0,
            SettingsJson = "{}",
            UpdatedAt = now
        };

    public void ReplaceSettings(string settingsJson, long expectedRevision, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsJson);
        if (expectedRevision != Revision)
        {
            throw new InvalidOperationException(
                $"Operator settings revision mismatch: expected {expectedRevision}, actual {Revision}.");
        }

        Revision++;
        SettingsJson = settingsJson;
        UpdatedAt = now;
    }
}
