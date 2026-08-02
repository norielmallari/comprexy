namespace Comprexy.Bench;

/// <summary>
/// Repo-relative locations the harness reads and writes. Everything the harness produces stays
/// under <c>reports/bench/</c> (gitignored) until <c>bench publish</c> copies curated output.
/// </summary>
internal static class BenchPaths
{
    public static string RepoRoot { get; } = ResolveRepoRoot();

    public static string ProxyProjectDirectory => Path.Combine(RepoRoot, "apps", "proxy");

    public static string ControlApiProjectDirectory => Path.Combine(RepoRoot, "apps", "control-api");

    public static string ProxyProjectFile =>
        Path.Combine(ProxyProjectDirectory, "Comprexy.Api.csproj");

    public static string ControlApiProjectFile =>
        Path.Combine(ControlApiProjectDirectory, "Comprexy.ControlApi.csproj");

    public static string ConversationsDirectory =>
        Path.Combine(RepoRoot, "tests", "Comprexy.Bench.Conversations");

    public static string DefaultDatabasePath =>
        Path.Combine(RepoRoot, "data", "comprexy-bench.db");

    public static string RunsRoot => Path.Combine(RepoRoot, "reports", "bench");

    public static string EvidenceDirectory => Path.Combine(RepoRoot, "docs", "evidence");

    public static string RunDirectory(string runId) => Path.Combine(RunsRoot, runId);

    private static string ResolveRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Comprexy.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                "Could not locate the repository root (no Comprexy.slnx above the bench binary).");
    }
}
