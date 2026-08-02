using Comprexy.Bench.Cli;

namespace Comprexy.Bench.Publishing;

/// <summary>
/// <c>bench publish</c>: copy a reviewed <c>summary.md</c> (and any screenshots) into
/// <c>docs/evidence/</c>. Human-gated on purpose — <c>reports/bench/</c> stays gitignored, and
/// nothing reaches the public tree without someone reading it first.
/// </summary>
internal static class BenchPublishCommand
{
    public static async Task<int> ExecuteAsync(BenchOptions options, CancellationToken cancellationToken)
    {
        var summaryPath = Path.Combine(options.RunDirectory, "summary.md");
        if (!File.Exists(summaryPath))
        {
            throw new BenchUsageException(
                $"No summary at {summaryPath}. Run 'bench report --run-id {options.RunId}' first.");
        }

        if (!options.Confirm)
        {
            Console.Error.WriteLine($"Review {summaryPath} before publishing:");
            Console.Error.WriteLine("  - figures match metrics.json");
            Console.Error.WriteLine("  - no local paths, request-log content, or personal data");
            Console.Error.WriteLine("  - caveats state what this run does not show");
            Console.Error.WriteLine();
            Console.Error.WriteLine(
                $"Then re-run with --confirm: ./comprexy.sh bench publish --run-id {options.RunId} --confirm");
            return 1;
        }

        Directory.CreateDirectory(BenchPaths.EvidenceDirectory);
        var target = Path.Combine(BenchPaths.EvidenceDirectory, $"bench-{options.RunId}.md");
        File.Copy(summaryPath, target, overwrite: true);
        Console.Error.WriteLine($"published: {target}");

        var screenshots = Path.Combine(options.RunDirectory, "screenshots");
        if (Directory.Exists(screenshots))
        {
            foreach (var png in Directory.EnumerateFiles(screenshots, "*.png"))
            {
                var pngTarget = Path.Combine(
                    BenchPaths.EvidenceDirectory,
                    $"bench-{options.RunId}-{Path.GetFileName(png)}");
                File.Copy(png, pngTarget, overwrite: true);
                Console.Error.WriteLine($"published: {pngTarget}");
            }
        }

        await Task.CompletedTask;
        return 0;
    }
}
