using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Comprexy.Infrastructure.Persistence;

/// <summary>
/// Enables WAL journaling and a busy timeout on each SQLite connection so concurrent chat
/// requests and background compression are less likely to fail with "database is locked".
/// </summary>
public sealed class SqliteWalConnectionInterceptor : DbConnectionInterceptor
{
    public const int BusyTimeoutMilliseconds = 5_000;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyPragmas(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplyPragmasAsync(connection, cancellationToken).ConfigureAwait(false);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    private static void ApplyPragmas(DbConnection connection)
    {
        Execute(connection, "PRAGMA journal_mode=WAL;");
        Execute(connection, $"PRAGMA busy_timeout={BusyTimeoutMilliseconds};");
    }

    private static async Task ApplyPragmasAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(
                connection,
                $"PRAGMA busy_timeout={BusyTimeoutMilliseconds};",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static void Execute(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static async Task ExecuteAsync(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
