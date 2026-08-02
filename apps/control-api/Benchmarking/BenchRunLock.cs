using System.Text.Json;

namespace Comprexy.ControlApi.Benchmarking;

public sealed class BenchRunLock : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private FileStream? _stream;
    private string? _lockPath;

    public string? HeldRunId { get; private set; }

    public bool TryAcquire(string lockPath, string runId, out BenchLockContents? existing)
    {
        existing = null;
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);

        try
        {
            _stream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            _lockPath = lockPath;
            HeldRunId = runId;
            var contents = new BenchLockContents(runId, Environment.ProcessId, DateTimeOffset.UtcNow);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(contents, JsonOptions);
            _stream.Write(bytes, 0, bytes.Length);
            _stream.Flush(true);
            return true;
        }
        catch (IOException)
        {
            existing = TryRead(lockPath);
            return false;
        }
    }

    public void Release()
    {
        _stream?.Dispose();
        _stream = null;
        if (_lockPath is not null && File.Exists(_lockPath))
        {
            try
            {
                File.Delete(_lockPath);
            }
            catch (IOException)
            {
                // Best-effort; operator may delete stale lock manually.
            }
        }

        _lockPath = null;
        HeldRunId = null;
    }

    public ValueTask DisposeAsync()
    {
        Release();
        return ValueTask.CompletedTask;
    }

    public static BenchLockContents? TryRead(string lockPath)
    {
        if (!File.Exists(lockPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(lockPath);
            return JsonSerializer.Deserialize<BenchLockContents>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record BenchLockContents(string RunId, int Pid, DateTimeOffset StartedAt);
