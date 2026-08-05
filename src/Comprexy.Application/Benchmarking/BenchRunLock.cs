using System.Diagnostics;
using System.Text.Json;

namespace Comprexy.Application.Benchmarking;

/// <summary>
/// Exclusive file lock under <c>reports/bench/</c> so CLI and dashboard orchestration
/// cannot spawn concurrent harness writers on the fixed bench ports.
/// </summary>
public sealed class BenchRunLock : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private FileStream? _stream;
    private string? _lockPath;

    public string? HeldRunId { get; private set; }

    /// <summary>
    /// Create the lock file exclusively. When the file already exists, reclaim it only if the
    /// recorded pid is no longer alive; otherwise leave <paramref name="existing"/> populated.
    /// </summary>
    public bool TryAcquire(string lockPath, string runId, out BenchLockContents? existing)
    {
        existing = null;
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                // Share.Read so contenders can inspect runId/pid while we hold the lock.
                _stream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
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
                if (attempt == 0 && existing is not null && !IsProcessAlive(existing.Pid))
                {
                    TryDeleteStale(lockPath);
                    existing = null;
                    continue;
                }

                return false;
            }
        }

        return false;
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
            using var stream = new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return JsonSerializer.Deserialize<BenchLockContents>(stream, JsonOptions);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Dashboard-spawned harness runs inherit the orchestrator's lock; verify it still names this run.
    /// </summary>
    public static void EnsureHeldByOrchestrator(string lockPath, string runId)
    {
        var existing = TryRead(lockPath)
            ?? throw new InvalidOperationException(
                $"Expected active-run lock at '{lockPath}' (orchestrator-held), but the file is missing.");

        if (!string.Equals(existing.RunId, runId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Active-run lock is for '{existing.RunId}', not this run '{runId}'.");
        }

        if (!IsProcessAlive(existing.Pid))
        {
            throw new InvalidOperationException(
                $"Active-run lock for '{runId}' names dead pid {existing.Pid}; refusing --under-orchestrator-lock.");
        }
    }

    public static bool IsProcessAlive(int pid)
    {
        if (pid <= 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void TryDeleteStale(string lockPath)
    {
        try
        {
            File.Delete(lockPath);
        }
        catch (IOException)
        {
            // Contended delete; next CreateNew will fail and surface the holder.
        }
    }
}

public sealed record BenchLockContents(string RunId, int Pid, DateTimeOffset StartedAt);
