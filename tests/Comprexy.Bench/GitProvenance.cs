namespace Comprexy.Bench;

internal sealed record GitProvenance(string Commit, bool Dirty)
{
    public static async Task<GitProvenance> ReadAsync(CancellationToken cancellationToken)
    {
        var commit = await RunAsync(["rev-parse", "HEAD"], cancellationToken) ?? "unknown";
        var status = await RunAsync(["status", "--porcelain"], cancellationToken);
        return new GitProvenance(commit, !string.IsNullOrWhiteSpace(status));
    }

    private static async Task<string?> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await GitCommand.RunAsync(BenchPaths.RepoRoot, arguments, cancellationToken);
        return result.Succeeded ? result.StandardOutput.Trim() : null;
    }
}
