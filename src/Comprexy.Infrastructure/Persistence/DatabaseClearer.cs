using Microsoft.EntityFrameworkCore;

namespace Comprexy.Infrastructure.Persistence;

/// <summary>
/// Drops and recreates the SQLite database from EF Core migrations.
/// Prefer the CLI when the API would otherwise lock the file:
/// <c>dotnet run --project src/Comprexy.Api -- --clear-db</c>.
/// Keep in sync with <c>Scripts/clear-database.sql</c> for manual sqlite3 use.
/// </summary>
public static class DatabaseClearer
{
    public static async Task RebuildAsync(ComprexyDbContext dbContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        await dbContext.Database.EnsureDeletedAsync(cancellationToken);
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
