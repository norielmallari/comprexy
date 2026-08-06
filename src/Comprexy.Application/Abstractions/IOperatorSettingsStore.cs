using Comprexy.Application.Models;

namespace Comprexy.Application.Abstractions;

/// <summary>
/// control-api–owned mutable operator settings store (SQLite singleton row).
/// </summary>
public interface IOperatorSettingsStore
{
    Task<(long Revision, string SettingsJson, DateTimeOffset UpdatedAt)> GetAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Replaces settings when <paramref name="expectedRevision"/> matches. Returns new revision
    /// or null on conflict (caller maps to 409).
    /// </summary>
    Task<(long Revision, DateTimeOffset UpdatedAt)?> TryPutAsync(
        long expectedRevision,
        string settingsJson,
        CancellationToken cancellationToken);
}
