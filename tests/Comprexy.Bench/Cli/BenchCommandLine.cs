using System.Globalization;

namespace Comprexy.Bench.Cli;

internal static class BenchCommandLine
{
    public static BenchOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new BenchOptions { Command = BenchCommand.Help };
        }

        var command = args[0] switch
        {
            "run" => BenchCommand.Run,
            "report" => BenchCommand.Report,
            "publish" => BenchCommand.Publish,
            "help" or "--help" or "-h" => BenchCommand.Help,
            var other => throw new BenchUsageException($"Unknown command '{other}'.")
        };

        var options = new BenchOptions { Command = command };
        var arms = new List<string>();
        var conversations = new List<string>();
        string? runIdArgument = null;
        var exactRunId = false;

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--arm":
                    arms.Add(RequireValue(args, ref i));
                    break;
                case "--conversation":
                    conversations.Add(RequireValue(args, ref i));
                    break;
                case "--run-id":
                    runIdArgument = RequireValue(args, ref i);
                    break;
                case "--no-spawn":
                    options = options with { NoSpawn = true };
                    break;
                case "--db":
                    options = options with { DatabasePath = Path.GetFullPath(RequireValue(args, ref i)) };
                    break;
                case "--proxy-port-maf-compact":
                    options = options with { MafCompactPort = RequireInt(args, ref i) };
                    break;
                case "--proxy-port-comprexy":
                    options = options with { ComprexyPort = RequireInt(args, ref i) };
                    break;
                case "--control-api-port":
                    options = options with { ControlApiPort = RequireInt(args, ref i) };
                    break;
                case "--max-context-window-tokens":
                    options = options with { MaxContextWindowTokens = RequireInt(args, ref i) };
                    break;
                case "--max-output-tokens":
                    options = options with { MaxOutputTokens = RequireInt(args, ref i) };
                    break;
                case "--completion-timeout":
                    options = options with { CompletionTimeoutSeconds = RequireInt(args, ref i) };
                    break;
                case "--conversation-timeout":
                    options = options with { ConversationTimeoutSeconds = RequireInt(args, ref i) };
                    break;
                case "--shell-timeout":
                    options = options with { ShellTimeoutSeconds = RequireInt(args, ref i) };
                    break;
                case "--startup-timeout":
                    options = options with { HostStartupTimeoutSeconds = RequireInt(args, ref i) };
                    break;
                case "--model":
                    options = options with { Model = RequireValue(args, ref i) };
                    break;
                case "--seed":
                    options = options with { Seed = RequireInt(args, ref i) };
                    break;
                case "--no-seed":
                    options = options with { Seed = null };
                    break;
                case "--trace":
                    options = options with { Trace = true };
                    break;
                case "--skip-build":
                    options = options with { SkipBuild = true };
                    break;
                case "--no-agent":
                    options = options with { NoAgent = true };
                    break;
                case "--screenshots":
                    options = options with { Screenshots = true };
                    break;
                case "--confirm":
                    options = options with { Confirm = true };
                    break;
                case "--continue-past-baseline-failure":
                    options = options with { StopAfterBaselineFailure = false };
                    break;
                case "--survival-margin":
                    options = options with { SurvivalMarginPrompts = Math.Max(1, RequireInt(args, ref i)) };
                    break;
                case "--cost-rates":
                    options = options with { CostRatesJson = RequireValue(args, ref i) };
                    break;
                case "--exact-run-id":
                    exactRunId = true;
                    break;
                case "--under-orchestrator-lock":
                    options = options with { UnderOrchestratorLock = true };
                    break;
                case "--help" or "-h":
                    return options with { Command = BenchCommand.Help };
                default:
                    throw new BenchUsageException($"Unknown option '{arg}'.");
            }
        }

        if (runIdArgument is null && options.Command is BenchCommand.Report or BenchCommand.Publish)
        {
            throw new BenchUsageException(
                $"'{args[0]}' needs --run-id; a run id is the directory 'bench run' wrote under reports/bench/.");
        }

        return options with
        {
            RunId = ResolveRunId(options.Command, runIdArgument, exactRunId),
            ExactRunId = exactRunId,
            Arms = arms,
            Conversations = conversations
        };
    }

    /// <summary>
    /// For <c>run</c>, the directory is always stamped with the UTC start minute and <c>--run-id</c>
    /// only labels it, so a repeat cannot overwrite an earlier run's artifacts. For <c>report</c> and
    /// <c>publish</c> the value is the existing directory name and is used verbatim.
    /// </summary>
    private static string ResolveRunId(BenchCommand command, string? runIdArgument, bool exactRunId)
    {
        if (command != BenchCommand.Run)
        {
            return runIdArgument ?? BenchOptions.FormatRunStamp(DateTimeOffset.UtcNow);
        }

        if (exactRunId && !string.IsNullOrWhiteSpace(runIdArgument))
        {
            return runIdArgument.Trim();
        }

        var stamp = BenchOptions.FormatRunStamp(DateTimeOffset.UtcNow);
        return string.IsNullOrWhiteSpace(runIdArgument) ? stamp : $"{stamp}-{runIdArgument.Trim()}";
    }

    public static string Usage => """
Usage: ./comprexy.sh bench <command> [options]

Commands:
  run        Spawn bench hosts, run both arms sequentially, write manifest.json
  report     Join control-api metrics into metrics.json and draft summary.md
  publish    Copy a reviewed summary into docs/evidence/ (requires --confirm)

Shared options:
  --run-id <id>                  run: optional label appended to the UTC yyyyMMdd-HHmm stamp that
                                 names the directory under reports/bench/
                                 report/publish: the existing directory name, used as given
  --db <path>                    Bench SQLite file (default data/comprexy-bench.db)
  --control-api-port <port>      Control-api port (default 18130)
  --no-spawn                     Use already-running hosts instead of spawning
  --startup-timeout <seconds>    Health-check timeout per host (default 120)
  --skip-build                   Reuse existing host build output

run options:
  --arm <maf-compact|comprexy>   Restrict to one arm (repeatable; default both)
  --conversation <name>          Restrict to one conversation script (repeatable)
  --proxy-port-maf-compact <p>   Baseline arm port (default 18129)
  --proxy-port-comprexy <p>      Treatment arm port (default 18131)
  --max-context-window-tokens <n>  MAF client compaction window on maf-compact (default 256000;
                                 the comprexy arm runs with client compaction off)
  --max-output-tokens <n>        Output budget reserved from that window (default 8192)
  --completion-timeout <seconds> Per-completion HTTP timeout (default 300); a breach ends that
                                 conversation as completion_stalled and the run moves on
  --conversation-timeout <s>     Per-conversation wall-clock cap (default 7200)
  --shell-timeout <seconds>      Per shell command cap in the sandbox (default 30)
  --model <name>                 Model sent upstream (default: proxy resolves Provider:Model)
  --seed <n> | --no-seed         Sampling seed when the provider honours it (default 7)
  --trace                        Write per-arm request trace files under the run directory
  --continue-past-baseline-failure
                                 Disable survival early-stop (default on): after maf-compact dies
                                 of a provider/context failure after X prompts, comprexy stops at
                                 X+margin instead of finishing the script
  --survival-margin <n>          Prompts past baseline PromptsCompleted before early-stop (default 1)
  --under-orchestrator-lock      Dashboard spawn only: verify .active-run.lock instead of acquiring it

report options:
  --run-id <id>                  Run to report on (required)
  --no-agent                     Deterministic figures only; skip the MAF narrative
  --screenshots                  Attempt live dashboard screenshots (never blocks metrics)

publish options:
  --run-id <id>                  Run to publish (required)
  --confirm                      Acknowledge the summary was reviewed before copying

Examples:
  ./comprexy.sh bench run                                      -> reports/bench/20260801-1200/
  ./comprexy.sh bench run --run-id short-deep                  -> reports/bench/20260801-1200-short-deep/
  ./comprexy.sh bench report --run-id 20260801-1200-short-deep
  ./comprexy.sh bench publish --run-id 20260801-1200-short-deep --confirm
""";

    private static string RequireValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new BenchUsageException($"Option '{args[index]}' requires a value.");
        }

        return args[++index];
    }

    private static int RequireInt(string[] args, ref int index)
    {
        var name = args[index];
        var raw = RequireValue(args, ref index);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new BenchUsageException($"Option '{name}' requires an integer (got '{raw}').");
    }
}

internal sealed class BenchUsageException(string message) : Exception(message);
