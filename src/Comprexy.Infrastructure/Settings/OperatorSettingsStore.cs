using Comprexy.Application.Abstractions;
using Comprexy.Domain.Entities;
using Comprexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Comprexy.Infrastructure.Settings;

/// <summary>
/// EF store for the singleton OperatorSettings row. Seeded by migration; ensures a row exists.
/// </summary>
public sealed class OperatorSettingsStore : IOperatorSettingsStore
{
    /// <summary>Fixed singleton row id (migration seed).</summary>
    public static readonly Guid SingletonId = Guid.Parse("a1000001-0000-4000-8000-000000000001");

    private readonly ComprexyDbContext _db;
    private readonly IClock _clock;

    public OperatorSettingsStore(ComprexyDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<(long Revision, string SettingsJson, DateTimeOffset UpdatedAt)> GetAsync(
        CancellationToken cancellationToken)
    {
        var row = await EnsureRowAsync(cancellationToken);
        return (row.Revision, row.SettingsJson, row.UpdatedAt);
    }

    public async Task<(long Revision, DateTimeOffset UpdatedAt)?> TryPutAsync(
        long expectedRevision,
        string settingsJson,
        CancellationToken cancellationToken)
    {
        var row = await EnsureRowAsync(cancellationToken);
        if (row.Revision != expectedRevision)
        {
            return null;
        }

        try
        {
            row.ReplaceSettings(settingsJson, expectedRevision, _clock.UtcNow);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return (row.Revision, row.UpdatedAt);
    }

    private async Task<OperatorSettings> EnsureRowAsync(CancellationToken cancellationToken)
    {
        var row = await _db.OperatorSettings
            .FirstOrDefaultAsync(cancellationToken);
        if (row is not null)
        {
            return row;
        }

        row = OperatorSettings.CreateSeed(SingletonId, _clock.UtcNow);
        _db.OperatorSettings.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
        return row;
    }
}
