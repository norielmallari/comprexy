namespace Comprexy.ControlApi.Configuration;

public sealed class BenchOrchestrationOptions
{
    public const string SectionName = "BenchOrchestration";

    public bool Enabled { get; init; } = true;

    /// <summary>Optional override for repository root; defaults to walking up from content root.</summary>
    public string? RepoRoot { get; init; }

    public string HarnessProjectPath { get; init; } = "tests/Comprexy.Bench/Comprexy.Bench.csproj";

    public string RunsRootRelative { get; init; } = "reports/bench";

    public string LockFileName { get; init; } = ".active-run.lock";

    public bool AllowSpawn { get; init; } = true;

    public string DatabasePathRelative { get; init; } = "data/comprexy-bench.db";

    public int MafCompactPort { get; init; } = 18_129;

    public int ComprexyPort { get; init; } = 18_131;

    public int ControlApiPort { get; init; } = 18_130;

    public int CompletionTimeoutSeconds { get; init; } = 300;

    public int ConversationTimeoutSeconds { get; init; } = 7_200;

    /// <summary>Per-conversation wall-clock cap for smoke-only dashboard runs (see internal/smoke-benchmark.md).</summary>
    public int SmokeConversationTimeoutSeconds { get; init; } = 1_200;
}
