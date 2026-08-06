namespace Comprexy.ControlApi.Tests;

/// <summary>
/// Serializes WebApplicationFactory hosts that isolate SQLite via
/// <c>ConnectionStrings__Comprexy</c> (Program re-appends env after SharedSqlite/Local).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ControlApiSqliteCollection : ICollectionFixture<ControlApiSqliteEnvGate>
{
    public const string Name = "ControlApiSqlite";
}

/// <summary>Process-wide gate so connection-string env overrides do not race.</summary>
public sealed class ControlApiSqliteEnvGate
{
    private readonly object _lock = new();

    public IDisposable UseDatabase(string databasePath)
    {
        lock (_lock)
        {
            var previous = Environment.GetEnvironmentVariable("ConnectionStrings__Comprexy");
            Environment.SetEnvironmentVariable(
                "ConnectionStrings__Comprexy",
                $"Data Source={databasePath}");
            return new Restore(previous, _lock);
        }
    }

    private sealed class Restore(string? previous, object gate) : IDisposable
    {
        public void Dispose()
        {
            lock (gate)
            {
                Environment.SetEnvironmentVariable("ConnectionStrings__Comprexy", previous);
            }
        }
    }
}
