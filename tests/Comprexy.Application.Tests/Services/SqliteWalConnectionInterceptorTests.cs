using Comprexy.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Comprexy.Application.Tests.Services;

public class SqliteWalConnectionInterceptorTests
{
    [Fact]
    public async Task ConnectionOpened_EnablesWalAndBusyTimeout()
    {
        var path = Path.Combine(Path.GetTempPath(), $"comprexy-wal-{Guid.NewGuid():N}.db");
        try
        {
            var interceptor = new SqliteWalConnectionInterceptor();
            await using var db = new ComprexyDbContext(
                new DbContextOptionsBuilder<ComprexyDbContext>()
                    .UseSqlite($"Data Source={path};Cache=Shared")
                    .AddInterceptors(interceptor)
                    .Options);

            await db.Database.OpenConnectionAsync();

            await using (var command = db.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = "PRAGMA journal_mode;";
                var mode = (string?)await command.ExecuteScalarAsync();
                Assert.Equal("wal", mode, ignoreCase: true);
            }

            await using (var command = db.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = "PRAGMA busy_timeout;";
                var timeout = Convert.ToInt64(await command.ExecuteScalarAsync());
                Assert.Equal(SqliteWalConnectionInterceptor.BusyTimeoutMilliseconds, timeout);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup for temp SQLite sidecars.
        }
    }
}
