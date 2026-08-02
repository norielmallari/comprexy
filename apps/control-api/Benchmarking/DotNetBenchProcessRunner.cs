using System.Diagnostics;

namespace Comprexy.ControlApi.Benchmarking;

public sealed class DotNetBenchProcessRunner : IBenchProcessRunner
{
    public async Task<BenchProcessResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return new BenchProcessResult(-1, "Failed to start dotnet process.");
        }

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            return new BenchProcessResult(process.ExitCode, string.IsNullOrWhiteSpace(stderr) ? null : stderr);
        }
        catch (OperationCanceledException)
        {
            TryKillTree(process);
            throw;
        }
    }

    private static void TryKillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort on cancel.
        }
    }
}
