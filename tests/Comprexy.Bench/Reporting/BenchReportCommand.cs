using Comprexy.Bench.Cli;
using Comprexy.Bench.Hosting;
using Comprexy.Bench.Model;

namespace Comprexy.Bench.Reporting;

/// <summary>
/// <c>bench report</c>: join the run's stored metrics into <c>metrics.json</c>, then draft
/// <c>summary.md</c>. Reporting needs a control-api on the same bench database the run used —
/// otherwise every conversation looks empty.
/// </summary>
internal static class BenchReportCommand
{
    public static async Task<int> ExecuteAsync(BenchOptions options, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(options.RunDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new BenchUsageException(
                $"No manifest at {manifestPath}. Pass --run-id for a completed run.");
        }

        var manifest = await BenchJson.ReadAsync<BenchManifest>(manifestPath, cancellationToken);
        var reportOptions = options with { DatabasePath = manifest.DatabasePath };

        await using var fleet = await StartControlApiAsync(reportOptions, cancellationToken);
        using var controlApi = new ControlApiClient(
            fleet?.ControlApiBaseUrl ?? manifest.ControlApiBaseUrl,
            HostConfigurationResolver.ResolveControlApiKey("Development"));

        if (!await controlApi.IsHealthyAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                $"No healthy control-api for the bench database at {manifest.DatabasePath}. Start one on {manifest.ControlApiBaseUrl} or drop --no-spawn.");
        }

        var metrics = await MetricsJoiner.BuildAsync(manifest, controlApi, cancellationToken);
        var metricsPath = Path.Combine(options.RunDirectory, "metrics.json");
        await BenchJson.WriteAsync(metricsPath, metrics, cancellationToken);
        Console.Error.WriteLine($"metrics: {metricsPath}");
        Console.Error.WriteLine(
            $"  paired {metrics.Headline.PairedConversationCount}, excluded {metrics.Headline.ExcludedConversationCount}");

        var interpretation = SummaryComposer.ComposeDeterministicInterpretation(metrics);
        if (!options.NoAgent)
        {
            try
            {
                var agent = new ReportAgent(options, controlApi.BaseUrl);
                interpretation = await agent.WriteInterpretationAsync(
                    metrics, SummaryComposer.ComposeNumbersBlock(metrics), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not BenchUsageException)
            {
                Console.Error.WriteLine(
                    $"warning: report agent failed ({ex.Message}); keeping the deterministic interpretation.");
            }
        }

        var summaryPath = Path.Combine(options.RunDirectory, "summary.md");
        await File.WriteAllTextAsync(
            summaryPath, SummaryComposer.ComposeDocument(metrics, interpretation), cancellationToken);
        Console.Error.WriteLine($"summary: {summaryPath}");

        if (options.Screenshots)
        {
            await EvidenceScreenshots.TryCaptureAsync(options, metrics, controlApi.BaseUrl, cancellationToken);
        }

        Console.Error.WriteLine(
            $"next: review {summaryPath}, then ./comprexy.sh bench publish --run-id {options.RunId} --confirm");

        return 0;
    }

    private static async Task<BenchHostFleet?> StartControlApiAsync(
        BenchOptions options,
        CancellationToken cancellationToken)
    {
        if (options.NoSpawn)
        {
            return null;
        }

        return await BenchHostFleet.StartAsync(
            options, [], cancellationToken, logSubdirectory: Path.Combine("logs", "report"));
    }
}
