namespace Comprexy.Bench.Cli;

internal enum BenchCommand
{
    Help,
    Run,
    Report,
    Publish
}

/// <summary>
/// Harness-owned knobs. None of these are product configuration: arm behaviour is set through
/// process environment on the spawned hosts (see <see cref="Hosting.BenchArm"/>).
/// </summary>
internal sealed record BenchOptions
{
    public required BenchCommand Command { get; init; }

    /// <summary>
    /// Directory name under <c>reports/bench/</c>. <c>run</c> always stamps this with the UTC minute
    /// it started so a repeated run cannot silently overwrite the artifacts of an earlier one;
    /// <c>--run-id</c> contributes a trailing label rather than replacing the stamp.
    /// </summary>
    public string RunId { get; init; } = FormatRunStamp(DateTimeOffset.UtcNow);

    public const string RunStampFormat = "yyyyMMdd-HHmm";

    public static string FormatRunStamp(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString(RunStampFormat, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Arms to run, in order. Empty means both arms.</summary>
    public IReadOnlyList<string> Arms { get; init; } = [];

    /// <summary>Conversation script names to run. Empty means every script on disk.</summary>
    public IReadOnlyList<string> Conversations { get; init; } = [];

    public bool NoSpawn { get; init; }

    public string DatabasePath { get; init; } = BenchPaths.DefaultDatabasePath;

    public int MafCompactPort { get; init; } = 18129;

    public int ComprexyPort { get; init; } = 18131;

    public int ControlApiPort { get; init; } = 18130;

    public int MaxContextWindowTokens { get; init; } = 256_000;

    public int MaxOutputTokens { get; init; } = 8_192;

    public int CompletionTimeoutSeconds { get; init; } = 300;

    public int ConversationTimeoutSeconds { get; init; } = 7_200;

    public int ShellTimeoutSeconds { get; init; } = 30;

    public int HostStartupTimeoutSeconds { get; init; } = 120;

    /// <summary>Model name sent upstream. Null lets the proxy resolve <c>Provider:Model</c>.</summary>
    public string? Model { get; init; }

    public int? Seed { get; init; } = 7;

    /// <summary>Request tracing per arm under the run directory.</summary>
    public bool Trace { get; init; }

    public bool SkipBuild { get; init; }

    /// <summary><c>report</c>: skip the MAF narrative and emit deterministic figures only.</summary>
    public bool NoAgent { get; init; }

    /// <summary><c>report</c>: attempt live dashboard screenshots.</summary>
    public bool Screenshots { get; init; }

    /// <summary><c>publish</c>: required acknowledgement that a human reviewed the summary.</summary>
    public bool Confirm { get; init; }

    /// <summary>
    /// When true (default), after <c>maf-compact</c> dies of a provider/context failure at prompt
    /// X completed, the <c>comprexy</c> arm stops once it completes X+margin prompts
    /// (<see cref="SurvivalMarginPrompts"/>). Opt out with <c>--continue-past-baseline-failure</c>.
    /// </summary>
    public bool StopAfterBaselineFailure { get; init; } = true;

    /// <summary>
    /// Extra prompts the treatment arm must complete past the baseline's
    /// <c>PromptsCompleted</c> before survival early-stop (default 1 → stop at X+1).
    /// </summary>
    public int SurvivalMarginPrompts { get; init; } = 1;

    /// <summary>Optional JSON file or inline JSON for cost rates stamped into the manifest.</summary>
    public string? CostRatesJson { get; init; }

    /// <summary>When true, <see cref="RunId"/> is used verbatim (orchestrator-started runs).</summary>
    public bool ExactRunId { get; init; }

    public string RunDirectory => BenchPaths.RunDirectory(RunId);
}
