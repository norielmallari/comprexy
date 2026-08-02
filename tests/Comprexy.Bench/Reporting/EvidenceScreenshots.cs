using System.Diagnostics;
using Comprexy.Bench.Cli;
using Comprexy.Bench.Model;

namespace Comprexy.Bench.Reporting;

/// <summary>
/// Optional dashboard screenshots for evidence. This runs the dashboard's separate live-evidence
/// Playwright project against the bench control-api; the merge-default mocked smoke suite is
/// untouched. A screenshot failure never invalidates the token metrics, so it only warns.
/// </summary>
internal static class EvidenceScreenshots
{
    public static async Task TryCaptureAsync(
        BenchOptions options,
        BenchMetrics metrics,
        string controlApiBaseUrl,
        CancellationToken cancellationToken)
    {
        var dashboardDirectory = Path.Combine(BenchPaths.RepoRoot, "apps", "dashboard");
        var evidenceProject = Path.Combine(dashboardDirectory, "e2e", "evidence");

        if (!Directory.Exists(evidenceProject))
        {
            Console.Error.WriteLine(
                "warning: --screenshots requested but apps/dashboard/e2e/evidence is not present; skipping.");
            return;
        }

        var conversationIds = metrics.Paired
            .SelectMany(p => new[] { p.MafCompact.ConversationId, p.Comprexy.ConversationId })
            .Select(id => id.ToString());

        var outputDirectory = Path.Combine(options.RunDirectory, "screenshots");
        Directory.CreateDirectory(outputDirectory);

        var startInfo = new ProcessStartInfo("npx")
        {
            WorkingDirectory = dashboardDirectory,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("playwright");
        startInfo.ArgumentList.Add("test");
        startInfo.ArgumentList.Add("--config=playwright.evidence.config.ts");
        startInfo.Environment["NEXT_PUBLIC_API_BASE_URL"] = controlApiBaseUrl;
        startInfo.Environment["COMPREXY_EVIDENCE_CONVERSATION_IDS"] = string.Join(",", conversationIds);
        startInfo.Environment["COMPREXY_EVIDENCE_OUTPUT_DIR"] = outputDirectory;

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                Console.Error.WriteLine("warning: could not start Playwright for evidence screenshots.");
                return;
            }

            await process.WaitForExitAsync(cancellationToken);
            Console.Error.WriteLine(process.ExitCode == 0
                ? $"screenshots: {outputDirectory}"
                : $"warning: evidence screenshots exited with code {process.ExitCode}; metrics are unaffected.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine(
                $"warning: evidence screenshots failed ({ex.Message}); metrics are unaffected.");
        }
    }
}
