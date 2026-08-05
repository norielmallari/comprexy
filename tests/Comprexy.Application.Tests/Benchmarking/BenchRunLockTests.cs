using System.Text.Json;
using Comprexy.Application.Benchmarking;

namespace Comprexy.Application.Tests.Benchmarking;

public sealed class BenchRunLockTests
{
    [Fact]
    public async Task TryAcquire_SecondHolder_FailsWhileFirstHolds()
    {
        var lockPath = Path.Combine(Path.GetTempPath(), "comprexy-bench-lock-tests", Guid.NewGuid().ToString("N"), ".active-run.lock");
        await using var first = new BenchRunLock();
        Assert.True(first.TryAcquire(lockPath, "run-a", out _));

        await using var second = new BenchRunLock();
        Assert.False(second.TryAcquire(lockPath, "run-b", out var existing));
        Assert.NotNull(existing);
        Assert.Equal("run-a", existing.RunId);
        Assert.Equal(Environment.ProcessId, existing.Pid);

        first.Release();
        Assert.True(second.TryAcquire(lockPath, "run-b", out _));
        second.Release();
    }

    [Fact]
    public async Task TryAcquire_ReclaimsStaleLock_WhenPidIsDead()
    {
        var dir = Path.Combine(Path.GetTempPath(), "comprexy-bench-lock-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var lockPath = Path.Combine(dir, ".active-run.lock");

        var stale = new BenchLockContents("stale-run", int.MaxValue - 7, DateTimeOffset.UtcNow.AddHours(-1));
        await File.WriteAllTextAsync(
            lockPath,
            JsonSerializer.Serialize(stale, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        Assert.False(BenchRunLock.IsProcessAlive(stale.Pid));

        await using var next = new BenchRunLock();
        Assert.True(next.TryAcquire(lockPath, "fresh-run", out var existing));
        Assert.Null(existing);
        Assert.Equal("fresh-run", next.HeldRunId);
        next.Release();
        Assert.False(File.Exists(lockPath));
    }

    [Fact]
    public async Task EnsureHeldByOrchestrator_RequiresMatchingLiveLock()
    {
        var dir = Path.Combine(Path.GetTempPath(), "comprexy-bench-lock-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var lockPath = Path.Combine(dir, ".active-run.lock");

        Assert.Throws<InvalidOperationException>(() =>
            BenchRunLock.EnsureHeldByOrchestrator(lockPath, "missing"));

        await using (var holder = new BenchRunLock())
        {
            Assert.True(holder.TryAcquire(lockPath, "run-1", out _));
            BenchRunLock.EnsureHeldByOrchestrator(lockPath, "run-1");
            Assert.Throws<InvalidOperationException>(() =>
                BenchRunLock.EnsureHeldByOrchestrator(lockPath, "run-other"));
        }
    }
}
