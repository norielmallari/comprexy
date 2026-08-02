using Comprexy.Application.Models.Benchmarking;
using Comprexy.ControlApi.Benchmarking;
using Comprexy.ControlApi.Configuration;
using Comprexy.ControlApi.Contracts.Benchmark;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Comprexy.ControlApi.Endpoints;

public static class BenchmarkEndpoints
{
    public static IEndpointRouteBuilder MapBenchmarkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/comprexy/benchmarks")
            .WithTags("ComprexyBenchmarks");

        group.MapGet("/scenarios", ListScenarios);
        group.MapPost("/runs", StartRunAsync);
        group.MapGet("/runs", ListRunsAsync);
        group.MapGet("/runs/{runId}", GetRunAsync);
        group.MapPost("/runs/{runId}/cancel", CancelRunAsync);
        group.MapPost("/runs/{runId}/report", ReportRunAsync);
        group.MapGet("/runs/{runId}/artifacts", GetArtifactsAsync);
        group.MapGet("/runs/{runId}/presentation", GetRunPresentationAsync);
        group.MapPost("/presentation/telemetry", TelemetryPresentationAsync);
        group.MapPost("/presentation/compare", ComparePresentationAsync);

        return app;
    }

    private static IResult ListScenarios([FromServices] IOptions<BenchOrchestrationOptions> options)
    {
        var repoRoot = ResolveRepoRoot(options.Value);
        var conversationsDir = Path.Combine(repoRoot, "tests", "Comprexy.Bench.Conversations");
        if (!Directory.Exists(conversationsDir))
        {
            return TypedResults.Ok(Array.Empty<BenchmarkScenarioDto>());
        }

        var scenarios = Directory.EnumerateFiles(conversationsDir, "*.json")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .Select(BenchmarkScenarioParser.Parse)
            .ToList();

        return TypedResults.Ok(scenarios);
    }

    private static async Task<IResult> StartRunAsync(
        BenchmarkStartRunRequest request,
        IBenchRunOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await orchestrator.StartAsync(request, cancellationToken);
            return TypedResults.Accepted($"/v1/comprexy/benchmarks/runs/{response.RunId}", response);
        }
        catch (BenchConflictException ex)
        {
            return TypedResults.Conflict(new { activeRunId = ex.ActiveRunId, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> ListRunsAsync(
        IBenchRunOrchestrator orchestrator,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await orchestrator.ListRunsAsync(cancellationToken));

    private static async Task<IResult> GetRunAsync(
        string runId,
        IBenchRunOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        var run = await orchestrator.GetRunAsync(runId, cancellationToken);
        return run is null ? TypedResults.NotFound() : TypedResults.Ok(run);
    }

    private static async Task<IResult> CancelRunAsync(
        string runId,
        IBenchRunOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        var cancelled = await orchestrator.CancelAsync(runId, cancellationToken);
        return cancelled ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    private static async Task<IResult> ReportRunAsync(
        string runId,
        BenchmarkStartRunRequest? request,
        IBenchRunOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        try
        {
            var exitCode = await orchestrator.ReportAsync(runId, request?.Rates, cancellationToken);
            return exitCode == 0
                ? TypedResults.Ok(new { runId, exitCode })
                : TypedResults.Problem($"bench report exited {exitCode}", statusCode: StatusCodes.Status500InternalServerError);
        }
        catch (BenchConflictException ex)
        {
            return TypedResults.Conflict(new { activeRunId = ex.ActiveRunId, message = ex.Message });
        }
    }

    private static async Task<IResult> GetArtifactsAsync(
        string runId,
        IBenchRunOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        var artifacts = await orchestrator.GetArtifactsAsync(runId, cancellationToken);
        return artifacts is null ? TypedResults.NotFound() : TypedResults.Ok(artifacts);
    }

    private static async Task<IResult> GetRunPresentationAsync(
        string runId,
        BenchmarkPresentationService presentation,
        [FromServices] IOptions<BenchOrchestrationOptions> options,
        CancellationToken cancellationToken)
    {
        var runDirectory = Path.Combine(
            ResolveRepoRoot(options.Value),
            options.Value.RunsRootRelative,
            runId);
        if (!Directory.Exists(runDirectory))
        {
            return TypedResults.NotFound();
        }

        var response = await presentation.BuildRunPresentationAsync(runDirectory, cancellationToken);
        return response is null ? TypedResults.NotFound() : TypedResults.Ok(response);
    }

    private static async Task<IResult> TelemetryPresentationAsync(
        BenchmarkTelemetryPresentationRequest request,
        BenchmarkPresentationService presentation,
        CancellationToken cancellationToken)
    {
        var response = await presentation.BuildTelemetryAsync(request, cancellationToken);
        return response is null ? TypedResults.NotFound() : TypedResults.Ok(response);
    }

    private static async Task<IResult> ComparePresentationAsync(
        BenchmarkComparisonPresentationRequest request,
        BenchmarkPresentationService presentation,
        CancellationToken cancellationToken)
    {
        var response = await presentation.BuildComparisonAsync(request, cancellationToken);
        return response is null ? TypedResults.NotFound() : TypedResults.Ok(response);
    }

    private static string ResolveRepoRoot(BenchOrchestrationOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.RepoRoot))
        {
            return Path.GetFullPath(options.RepoRoot);
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Comprexy.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
