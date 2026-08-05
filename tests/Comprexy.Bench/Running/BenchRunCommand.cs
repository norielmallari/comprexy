using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Comprexy.Application.Benchmarking;
using Comprexy.Application.Models.Benchmarking;
using Comprexy.Bench.Cli;
using Comprexy.Bench.Hosting;
using Comprexy.Bench.Model;
using Comprexy.Bench.Tools;
using Microsoft.Agents.AI;

namespace Comprexy.Bench.Running;

/// <summary>
/// <c>bench run</c>: spawn hosts, replay the frozen prompt lists through both arms sequentially,
/// and write <c>manifest.json</c>. Analysis stays in <c>bench report</c> so an expensive local run
/// can be re-reported without paying for it twice.
/// </summary>
internal static class BenchRunCommand
{
    public static async Task<int> ExecuteAsync(BenchOptions options, CancellationToken cancellationToken)
    {
        var arms = SelectArms(options);

        if (File.Exists(Path.Combine(options.RunDirectory, "manifest.json")))
        {
            throw new BenchUsageException(
                $"{options.RunDirectory} already holds a completed run. Runs are stamped to the minute, " +
                "so give --run-id a distinct label rather than overwriting an earlier run's artifacts.");
        }

        await using var runLock = await AcquireOrVerifyActiveRunLockAsync(options);

        BenchPortPreflight.EnsurePortsFree(options);

        Directory.CreateDirectory(options.RunDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(options.DatabasePath)!);

        Console.Error.WriteLine($"bench run {options.RunId}");
        Console.Error.WriteLine($"  database    {options.DatabasePath}");
        Console.Error.WriteLine($"  run output  {options.RunDirectory}");
        Console.Error.WriteLine($"  arms        {string.Join(", ", arms.Select(a => a.Name))}");
        Console.Error.WriteLine(
            options.UnderOrchestratorLock
                ? $"  active lock  {BenchPaths.ActiveRunLockPath} (orchestrator-held)"
                : $"  active lock  {BenchPaths.ActiveRunLockPath}");

        var scripts = ConversationScript.LoadAll(options.Conversations);
        Console.Error.WriteLine(
            $"  scripts     {string.Join(", ", scripts.Select(s => $"{s.Name} ({s.Prompts.Count} prompts)"))}");

        var git = await GitProvenance.ReadAsync(cancellationToken);
        Console.Error.WriteLine(
            $"  workspace   throwaway clone at {git.Commit}{(git.Dirty ? " (uncommitted work is NOT in the workspace)" : string.Empty)}");

        var startedAt = DateTimeOffset.UtcNow;
        var runner = new MafConversationRunner(options);
        var armManifests = new List<BenchArmManifest>();
        // Baseline results keyed by script name so the treatment arm can early-stop at X+margin.
        var baselineByScript = new Dictionary<string, BenchConversationRun>(StringComparer.Ordinal);

        if (options.StopAfterBaselineFailure)
        {
            Console.Error.WriteLine(
                $"  survival    early-stop on (margin {options.SurvivalMarginPrompts}); " +
                "--continue-past-baseline-failure to run full scripts after a baseline kill");
        }
        else
        {
            Console.Error.WriteLine("  survival    early-stop off (--continue-past-baseline-failure)");
        }

        await using var fleet = await BenchHostFleet.StartAsync(options, arms, cancellationToken);

        await BenchRunStatusWriter.WriteProgressAsync(
            options.RunDirectory,
            arm: null,
            conversationName: null,
            promptsCompleted: null,
            runPhase: "running",
            cancellationToken);

        foreach (var arm in arms)
        {
            var resolved = HostConfigurationResolver.Resolve(fleet.ArmEnvironment(arm), "Development");
            var clientCompaction = arm.UsesClientCompaction
                ? $"MaxContextWindowTokens={options.MaxContextWindowTokens}"
                : "off";
            Console.Error.WriteLine(
                $"\narm {arm.Name}: ToolSchema:Mode={resolved.ToolSchemaMode} ContextPolicy:SoftLimitTokens={resolved.SoftLimitTokens} ClientCompaction={clientCompaction}");

            var armStopwatch = Stopwatch.StartNew();
            var conversationRuns = new List<BenchConversationRun>();

            foreach (var script in scripts)
            {
                await BenchRunStatusWriter.WriteProgressAsync(
                    options.RunDirectory,
                    arm: arm.Name,
                    conversationName: script.Name,
                    promptsCompleted: null,
                    runPhase: "running",
                    cancellationToken);

                SurvivalEarlyStop? survival = null;
                if (options.StopAfterBaselineFailure &&
                    arm.Name == BenchArm.Comprexy &&
                    baselineByScript.TryGetValue(script.Name, out var baseline) &&
                    BaselineKillZone.SurvivalStopAfterPrompts(baseline, options.SurvivalMarginPrompts) is { } stopAfter)
                {
                    survival = new SurvivalEarlyStop(stopAfter, baseline, options.SurvivalMarginPrompts);
                    Console.Error.Write(
                        $"  {script.Name} … (survival stop after {stopAfter} prompts; baseline {baseline.Status} at {baseline.PromptsCompleted}/{baseline.PromptCount}) ");
                }
                else
                {
                    Console.Error.Write($"  {script.Name} … ");
                }

                var run = await runner.RunAsync(
                    arm, resolved, script, git.Commit, survival, cancellationToken);
                conversationRuns.Add(run);

                if (arm.Name == BenchArm.MafCompact)
                {
                    baselineByScript[script.Name] = run;
                }

                Console.Error.WriteLine(
                    $"{run.Status} ({run.PromptsCompleted}/{run.PromptCount} prompts, {run.ConversationWallClockMs / 1000.0:0.0}s, client compaction {(run.ClientCompactionCount is { } fired ? $"x{fired}" : "off")})");

                await BenchRunStatusWriter.WriteProgressAsync(
                    options.RunDirectory,
                    arm: arm.Name,
                    conversationName: script.Name,
                    promptsCompleted: run.PromptsCompleted,
                    runPhase: ConversationStatus.IsSuccessfulTerminal(run.Status) ? "run_finished" : "run_failed",
                    cancellationToken);

                if (run.FailureReason is not null)
                {
                    Console.Error.WriteLine($"    reason: {run.FailureReason}");
                    if (run.Status is ConversationStatus.Failed
                        or ConversationStatus.TimedOut
                        or ConversationStatus.CompletionStalled)
                    {
                        Console.Error.WriteLine(
                            $"    arm log tail:{System.Environment.NewLine}{fleet.ReadArmLogTail(arm.Name)}");
                    }
                }
            }

            armStopwatch.Stop();
            armManifests.Add(new BenchArmManifest(
                arm.Name,
                arm.Description,
                arm.BaseUrl,
                arm.UsesClientCompaction,
                arm.Environment,
                new ResolvedArmSettings(
                    resolved.ToolSchemaMode,
                    resolved.SoftLimitTokens,
                    resolved.PassThrough,
                    resolved.ProviderBaseUrl,
                    resolved.ProviderModel),
                (long)armStopwatch.Elapsed.TotalMilliseconds,
                conversationRuns));
        }

        var manifest = new BenchManifest
        {
            RunId = options.RunId,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            ComprexyCommit = git.Commit,
            RepositoryDirty = git.Dirty,
            MafPackageVersion = ResolveMafVersion(),
            DatabasePath = options.DatabasePath,
            ControlApiBaseUrl = fleet.ControlApiBaseUrl,
            Model = options.Model ?? armManifests.FirstOrDefault()?.Resolved.ProviderModel,
            Harness = new BenchHarnessSettings(
                options.MaxContextWindowTokens,
                options.MaxOutputTokens,
                options.CompletionTimeoutSeconds * 1000,
                options.ConversationTimeoutSeconds * 1000,
                options.ShellTimeoutSeconds * 1000,
                options.Seed,
                0d,
                ClientToolCatalogVersion: SandboxToolCatalog.CatalogVersion),
            CostRates = ResolveCostRates(options),
            Arms = armManifests
        };

        var manifestPath = Path.Combine(options.RunDirectory, "manifest.json");
        await BenchJson.WriteAsync(manifestPath, manifest, cancellationToken);

        Console.Error.WriteLine($"\nmanifest: {manifestPath}");
        Console.Error.WriteLine($"next: ./comprexy.sh bench report --run-id {options.RunId}");

        return manifest.Arms.SelectMany(a => a.Conversations)
            .All(c => ConversationStatus.IsSuccessfulTerminal(c.Status))
            ? 0
            : 1;
    }

    private static IReadOnlyList<BenchArm> SelectArms(BenchOptions options)
    {
        var all = new List<BenchArm>
        {
            BenchArm.CreateMafCompact(options.MafCompactPort),
            BenchArm.CreateComprexy(options.ComprexyPort)
        };

        if (options.Arms.Count == 0)
        {
            return all;
        }

        var selected = options.Arms
            .Select(name => all.FirstOrDefault(a => a.Name == name)
                ?? throw new BenchUsageException(
                    $"Unknown arm '{name}'. Expected {BenchArm.MafCompact} or {BenchArm.Comprexy}."))
            .ToList();

        return selected;
    }

    private static BenchmarkCostRates? ResolveCostRates(BenchOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.CostRatesJson))
        {
            return null;
        }

        var json = options.CostRatesJson.Trim();
        if (File.Exists(json))
        {
            json = File.ReadAllText(json);
        }

        return JsonSerializer.Deserialize<BenchmarkCostRates>(json, BenchJson.Options);
    }

    private static string ResolveMafVersion() =>
        typeof(AIAgent).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AIAgent).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    private static ValueTask<IAsyncDisposable> AcquireOrVerifyActiveRunLockAsync(BenchOptions options)
    {
        var lockPath = BenchPaths.ActiveRunLockPath;
        if (options.UnderOrchestratorLock)
        {
            BenchRunLock.EnsureHeldByOrchestrator(lockPath, options.RunId);
            return ValueTask.FromResult<IAsyncDisposable>(NoopAsyncDisposable.Instance);
        }

        var runLock = new BenchRunLock();
        if (!runLock.TryAcquire(lockPath, options.RunId, out var existing))
        {
            runLock.Release();
            var holder = existing is null
                ? "another process"
                : $"run '{existing.RunId}' (pid {existing.Pid}, started {existing.StartedAt:u})";
            throw new InvalidOperationException(
                $"Another bench run is active ({holder}). Wait for it to finish. " +
                $"Lock file: {lockPath}");
        }

        return ValueTask.FromResult<IAsyncDisposable>(runLock);
    }

    private sealed class NoopAsyncDisposable : IAsyncDisposable
    {
        public static readonly NoopAsyncDisposable Instance = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
