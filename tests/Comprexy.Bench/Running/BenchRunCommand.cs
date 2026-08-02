using System.Diagnostics;
using System.Reflection;
using Comprexy.Bench.Cli;
using Comprexy.Bench.Hosting;
using Comprexy.Bench.Model;
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

        Directory.CreateDirectory(options.RunDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(options.DatabasePath)!);

        Console.Error.WriteLine($"bench run {options.RunId}");
        Console.Error.WriteLine($"  database    {options.DatabasePath}");
        Console.Error.WriteLine($"  run output  {options.RunDirectory}");
        Console.Error.WriteLine($"  arms        {string.Join(", ", arms.Select(a => a.Name))}");

        var scripts = ConversationScript.LoadAll(options.Conversations);
        Console.Error.WriteLine(
            $"  scripts     {string.Join(", ", scripts.Select(s => $"{s.Name} ({s.Prompts.Count} prompts)"))}");

        var git = await GitProvenance.ReadAsync(cancellationToken);
        Console.Error.WriteLine(
            $"  workspace   throwaway clone at {git.Commit}{(git.Dirty ? " (uncommitted work is NOT in the workspace)" : string.Empty)}");

        var startedAt = DateTimeOffset.UtcNow;
        var runner = new MafConversationRunner(options);
        var armManifests = new List<BenchArmManifest>();

        await using var fleet = await BenchHostFleet.StartAsync(options, arms, cancellationToken);

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
                Console.Error.Write($"  {script.Name} … ");
                var run = await runner.RunAsync(arm, resolved, script, git.Commit, cancellationToken);
                conversationRuns.Add(run);
                Console.Error.WriteLine(
                    $"{run.Status} ({run.PromptsCompleted}/{run.PromptCount} prompts, {run.ConversationWallClockMs / 1000.0:0.0}s, client compaction {(run.ClientCompactionCount is { } fired ? $"x{fired}" : "off")})");

                if (run.FailureReason is not null)
                {
                    Console.Error.WriteLine($"    reason: {run.FailureReason}");
                    Console.Error.WriteLine($"    arm log tail:{System.Environment.NewLine}{fleet.ReadArmLogTail(arm.Name)}");
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
                0d),
            Arms = armManifests
        };

        var manifestPath = Path.Combine(options.RunDirectory, "manifest.json");
        await BenchJson.WriteAsync(manifestPath, manifest, cancellationToken);

        Console.Error.WriteLine($"\nmanifest: {manifestPath}");
        Console.Error.WriteLine($"next: ./comprexy.sh bench report --run-id {options.RunId}");

        return manifest.Arms.SelectMany(a => a.Conversations)
            .All(c => c.Status == ConversationStatus.Completed)
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

    private static string ResolveMafVersion() =>
        typeof(AIAgent).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AIAgent).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}
