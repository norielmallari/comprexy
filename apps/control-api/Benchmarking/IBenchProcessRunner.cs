namespace Comprexy.ControlApi.Benchmarking;

public interface IBenchProcessRunner
{
    Task<BenchProcessResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}

public sealed record BenchProcessResult(int ExitCode, string? StandardError);
