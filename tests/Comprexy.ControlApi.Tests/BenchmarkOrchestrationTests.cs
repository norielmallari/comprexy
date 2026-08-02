using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Comprexy.Application.Models.Benchmarking;
using Comprexy.ControlApi.Benchmarking;
using Comprexy.ControlApi.Contracts.Benchmark;
using Comprexy.Domain.Entities;
using Comprexy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Comprexy.ControlApi.Tests;

public sealed class BenchmarkOrchestrationTests
{
  private static readonly JsonSerializerOptions ArtifactJsonOptions = new(JsonSerializerDefaults.Web);

  [Fact]
  public async Task StartRun_ReturnsAcceptedWithRunId_BeforeBackgroundJobCompletes()
  {
    await using var factory = new BenchmarkOrchestrationFactory();
    using var client = factory.CreateClient();
    factory.ProcessRunner.BlockRun = true;

    var response = await client.PostAsJsonAsync(
      "/v1/comprexy/benchmarks/runs",
      new BenchmarkStartRunRequest { Conversations = ["fixture-conversation"] });

    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<BenchmarkStartRunResponse>();
    Assert.NotNull(body);
    Assert.False(string.IsNullOrWhiteSpace(body.RunId));
    await factory.ProcessRunner.RunStarted.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.True(File.Exists(factory.LockPath));

    factory.ProcessRunner.CompleteRun();
    var terminal = await WaitForTerminalPhaseAsync(client, body.RunId, TimeSpan.FromSeconds(10));
    Assert.True(BenchOuterPhases.IsTerminal(terminal.Phase));
    Assert.False(File.Exists(factory.LockPath));
  }

  [Fact]
  public async Task SecondStartWhileActive_ReturnsConflictWithActiveRunId()
  {
    await using var factory = new BenchmarkOrchestrationFactory();
    using var client = factory.CreateClient();
    factory.ProcessRunner.BlockRun = true;

    var first = await client.PostAsJsonAsync(
      "/v1/comprexy/benchmarks/runs",
      new BenchmarkStartRunRequest { RunLabel = "user-1" });
    var firstBody = await first.Content.ReadFromJsonAsync<BenchmarkStartRunResponse>();
    Assert.NotNull(firstBody);
    await factory.ProcessRunner.RunStarted.WaitAsync(TimeSpan.FromSeconds(5));

    var second = await client.PostAsJsonAsync(
      "/v1/comprexy/benchmarks/runs",
      new BenchmarkStartRunRequest { RunLabel = "user-1-other" });

    Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    using var conflict = await JsonDocument.ParseAsync(await second.Content.ReadAsStreamAsync());
    Assert.Equal(firstBody.RunId, conflict.RootElement.GetProperty("activeRunId").GetString());

    factory.ProcessRunner.CompleteRun();
    await WaitForTerminalPhaseAsync(client, firstBody.RunId, TimeSpan.FromSeconds(10));
  }

  [Fact]
  public async Task CancelRun_ReleasesLock_AndSetsCancelledPhase()
  {
    await using var factory = new BenchmarkOrchestrationFactory();
    using var client = factory.CreateClient();
    factory.ProcessRunner.BlockRun = true;

    var start = await client.PostAsJsonAsync(
      "/v1/comprexy/benchmarks/runs",
      new BenchmarkStartRunRequest());
    var body = await start.Content.ReadFromJsonAsync<BenchmarkStartRunResponse>();
    Assert.NotNull(body);
    await factory.ProcessRunner.RunStarted.WaitAsync(TimeSpan.FromSeconds(5));
    await WaitUntilActiveRunAsync(factory, body.RunId, TimeSpan.FromSeconds(5));
    Assert.True(File.Exists(factory.LockPath));

    var cancelResponse = await client.PostAsync(
      $"/v1/comprexy/benchmarks/runs/{body.RunId}/cancel",
      content: null);

    Assert.Equal(HttpStatusCode.NoContent, cancelResponse.StatusCode);

    var terminal = await WaitForTerminalPhaseAsync(client, body.RunId, TimeSpan.FromSeconds(10));
    Assert.Equal(BenchOuterPhases.Cancelled, terminal.Phase);
    Assert.False(File.Exists(factory.LockPath));
    Assert.NotNull(factory.ProcessRunner.LastCancellationToken);
    Assert.True(factory.ProcessRunner.LastCancellationToken.Value.IsCancellationRequested);
  }

  [Fact]
  public async Task RunSuccessWithReportFailure_SetsCompletedWithReportErrorPhase()
  {
    await using var factory = new BenchmarkOrchestrationFactory();
    using var client = factory.CreateClient();
    factory.ProcessRunner.BlockRun = false;
    factory.ProcessRunner.Configure(runExitCode: 0, reportExitCode: 1);

    var start = await client.PostAsJsonAsync(
      "/v1/comprexy/benchmarks/runs",
      new BenchmarkStartRunRequest());
    var body = await start.Content.ReadFromJsonAsync<BenchmarkStartRunResponse>();
    Assert.NotNull(body);

    var terminal = await WaitForTerminalPhaseAsync(client, body.RunId, TimeSpan.FromSeconds(10));
    Assert.Equal(BenchOuterPhases.CompletedWithReportError, terminal.Phase);
    Assert.False(string.IsNullOrWhiteSpace(terminal.LastError));
    Assert.Equal(1, factory.ProcessRunner.RunCallCount);
    Assert.Equal(1, factory.ProcessRunner.ReportCallCount);
    Assert.False(File.Exists(factory.LockPath));
  }

  [Fact]
  public async Task StartFailureAfterLockAcquire_ReleasesLockFile()
  {
    await using var factory = new BenchmarkOrchestrationFactory(preventIndexWrite: true);
    using var client = factory.CreateClient();

    var response = await client.PostAsJsonAsync(
      "/v1/comprexy/benchmarks/runs",
      new BenchmarkStartRunRequest());

    Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    Assert.False(File.Exists(factory.LockPath));
  }

  [Fact]
  public async Task StartRun_CancellingHttpTokenDoesNotCancelBackgroundJob()
  {
    await using var factory = new BenchmarkOrchestrationFactory();
    factory.ProcessRunner.BlockRun = true;

    using var scope = factory.Services.CreateScope();
    var orchestrator = scope.ServiceProvider.GetRequiredService<IBenchRunOrchestrator>();
    using var httpCts = new CancellationTokenSource();

    var response = await orchestrator.StartAsync(
      new BenchmarkStartRunRequest { Conversations = ["fixture-conversation"] },
      httpCts.Token);
    await factory.ProcessRunner.RunStarted.WaitAsync(TimeSpan.FromSeconds(5));

    await httpCts.CancelAsync();

    Assert.NotNull(factory.ProcessRunner.LastCancellationToken);
    Assert.False(factory.ProcessRunner.LastCancellationToken.Value.IsCancellationRequested);

    factory.ProcessRunner.CompleteRun();
    var terminal = await WaitForTerminalRunAsync(orchestrator, response.RunId, TimeSpan.FromSeconds(10));
    Assert.Equal(BenchOuterPhases.Completed, terminal.Phase);
    Assert.False(File.Exists(factory.LockPath));
  }

  [Fact]
  public async Task GetRunPresentation_ReturnsSeparatedIoDto_FromPresentationArtifact()
  {
    await using var factory = new BenchmarkOrchestrationFactory();
    using var client = factory.CreateClient();

    var baselineId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    var compareId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    const string runId = "fixture-run-presentation-001";
    var runDirectory = Path.Combine(factory.RunsRoot, runId);
    await WritePresentationFixtureAsync(
      runDirectory,
      runId,
      baselineId,
      compareId,
      baselineInput: 1_200,
      baselineOutput: 300,
      baselineOverhead: 50,
      compareInput: 900,
      compareOutput: 250,
      compareOverhead: 30,
      modelKind: null);

    var response = await client.GetAsync($"/v1/comprexy/benchmarks/runs/{runId}/presentation");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var body = await response.Content.ReadFromJsonAsync<BenchmarkComparisonPresentationResponse>();
    Assert.NotNull(body);
    Assert.Equal(runId, body.RunId);
    Assert.Equal(baselineId, body.BaselineConversationId);
    Assert.Equal(compareId, body.CompareConversationId);
    Assert.Equal(1_200, body.Totals.Baseline.InputTokens);
    Assert.Equal(300, body.Totals.Baseline.OutputTokens);
    Assert.Equal(50, body.Totals.Baseline.OverheadTokens);
    Assert.Equal(900, body.Totals.Compare.InputTokens);
    Assert.Equal(250, body.Totals.Compare.OutputTokens);
    Assert.Equal(30, body.Totals.Compare.OverheadTokens);
    Assert.Equal(-300, body.Totals.Input.Delta);
    Assert.Equal(-50, body.Totals.Output.Delta);
    Assert.Equal(-20, body.Totals.Overhead.Delta);
    Assert.Null(body.Cost);
    Assert.Contains("turns-maf-compact-fixture.json", body.TurnSeriesPaths);
  }

  [Fact]
  public async Task GetRunPresentation_WithUsdRates_ReturnsSeparatedCostChannels()
  {
    await using var factory = new BenchmarkOrchestrationFactory();
    using var client = factory.CreateClient();

    var baselineId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    var compareId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    const string runId = "fixture-run-presentation-usd";
    var runDirectory = Path.Combine(factory.RunsRoot, runId);
    await WritePresentationFixtureAsync(
      runDirectory,
      runId,
      baselineId,
      compareId,
      baselineInput: 1_000_000,
      baselineOutput: 0,
      baselineOverhead: 200_000,
      compareInput: 0,
      compareOutput: 1_000_000,
      compareOverhead: 0,
      modelKind: BenchmarkModelKind.Usd,
      inputUsdPer1M: 3m,
      outputUsdPer1M: 15m);

    var response = await client.GetAsync($"/v1/comprexy/benchmarks/runs/{runId}/presentation");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var body = await response.Content.ReadFromJsonAsync<BenchmarkComparisonPresentationResponse>();
    Assert.NotNull(body);
    Assert.NotNull(body.Cost);
    Assert.Equal(BenchmarkModelKind.Usd, body.Cost.ModelKind);
    Assert.Equal(3m, body.Cost.BaselineInputCostUsd);
    Assert.Equal(0m, body.Cost.BaselineOutputCostUsd);
    Assert.Equal(3.6m, body.Cost.BaselineOverheadCostUsd);
    Assert.Equal(0m, body.Cost.CompareInputCostUsd);
    Assert.Equal(15m, body.Cost.CompareOutputCostUsd);
    Assert.Equal(0m, body.Cost.CompareOverheadCostUsd);
    Assert.Equal(6.6m, body.Cost.BaselineTotalCostUsd);
    Assert.Equal(15m, body.Cost.CompareTotalCostUsd);
    Assert.Equal(8.4m, body.Cost.CostDeltaUsd);
  }

  [Fact]
  public async Task GetRunPresentation_EmptyPaired_ReturnsNotFound()
  {
    await using var factory = new BenchmarkOrchestrationFactory();
    using var client = factory.CreateClient();

    const string runId = "fixture-run-presentation-empty";
    var runDirectory = Path.Combine(factory.RunsRoot, runId);
    Directory.CreateDirectory(runDirectory);
    await File.WriteAllTextAsync(
      Path.Combine(runDirectory, "presentation.json"),
      JsonSerializer.Serialize(
        new { runId, metrics = new { paired = Array.Empty<object>() } },
        ArtifactJsonOptions));

    var response = await client.GetAsync($"/v1/comprexy/benchmarks/runs/{runId}/presentation");
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task ComparePresentation_ReturnsSeparatedIoFields_FromSeededConversations()
  {
    await using var factory = new BenchmarkOrchestrationFactory();
    using var client = factory.CreateClient();
    var (baselineId, compareId) = await SeedPresentationConversationsAsync(factory.Services);

    var response = await client.PostAsJsonAsync(
      "/v1/comprexy/benchmarks/presentation/compare",
      new BenchmarkComparisonPresentationRequest
      {
        BaselineConversationId = baselineId,
        CompareConversationId = compareId,
        ModelKind = BenchmarkModelKind.Local
      });

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<BenchmarkComparisonPresentationResponse>();
    Assert.NotNull(body);
    Assert.Equal(1_200, body.Totals.Baseline.InputTokens);
    Assert.Equal(300, body.Totals.Baseline.OutputTokens);
    Assert.Equal(50, body.Totals.Baseline.OverheadTokens);
    Assert.Equal(900, body.Totals.Compare.InputTokens);
    Assert.Equal(250, body.Totals.Compare.OutputTokens);
    Assert.Equal(30, body.Totals.Compare.OverheadTokens);
    Assert.Equal(-300, body.Totals.Input.Delta);
    Assert.Equal(-50, body.Totals.Output.Delta);
    Assert.Equal(-20, body.Totals.Overhead.Delta);
  }

  [Fact]
  public async Task StartSmokeRun_PassesSmokeHarnessFlags()
  {
    await using var factory = new BenchmarkOrchestrationFactory();
    using var client = factory.CreateClient();
    factory.ProcessRunner.BlockRun = false;
    factory.ProcessRunner.Configure(runExitCode: 0, reportExitCode: 0);

    var start = await client.PostAsJsonAsync(
      "/v1/comprexy/benchmarks/runs",
      new BenchmarkStartRunRequest { Conversations = ["smoke-large-blob"] });
    var body = await start.Content.ReadFromJsonAsync<BenchmarkStartRunResponse>();
    Assert.NotNull(body);

    await WaitForTerminalPhaseAsync(client, body.RunId, TimeSpan.FromSeconds(10));

    Assert.NotNull(factory.ProcessRunner.LastRunArguments);
    Assert.Contains("--conversation-timeout", factory.ProcessRunner.LastRunArguments);
    var timeoutIndex = factory.ProcessRunner.LastRunArguments
        .Select((argument, index) => (argument, index))
        .First(pair => pair.argument == "--conversation-timeout")
        .index;
    Assert.Equal("1200", factory.ProcessRunner.LastRunArguments[timeoutIndex + 1]);
    Assert.Contains("--continue-past-baseline-failure", factory.ProcessRunner.LastRunArguments);
  }

  [Fact]
  public async Task ListScenarios_IncludesSmokeLargeBlobWithPromptCount()
  {
    await using var factory = new BenchmarkOrchestrationFactory();
    using var client = factory.CreateClient();
    var conversationsDir = Path.Combine(factory.BenchRoot, "tests", "Comprexy.Bench.Conversations");
    Directory.CreateDirectory(conversationsDir);
    await File.WriteAllTextAsync(
      Path.Combine(conversationsDir, "smoke-large-blob.json"),
      """
      {
        "provenance": "fixture smoke script",
        "largestFiles": { "count": 10 },
        "promptTemplate": "fixture {{relativePath}}"
      }
      """);

    var response = await client.GetAsync("/v1/comprexy/benchmarks/scenarios");
    response.EnsureSuccessStatusCode();
    var scenarios = await response.Content.ReadFromJsonAsync<List<BenchmarkScenarioDto>>();
    Assert.NotNull(scenarios);

    var smoke = Assert.Single(scenarios, scenario => scenario.Name == "smoke-large-blob");
    Assert.Equal(10, smoke.PromptCount);
    Assert.True(smoke.IsSmoke);
    Assert.Equal("fixture smoke script", smoke.Description);
  }

  private static async Task<BenchmarkRunSummaryDto> WaitForTerminalRunAsync(
    IBenchRunOrchestrator orchestrator,
    string runId,
    TimeSpan timeout)
  {
    var deadline = DateTimeOffset.UtcNow + timeout;
    while (DateTimeOffset.UtcNow < deadline)
    {
      var run = await orchestrator.GetRunAsync(runId, CancellationToken.None);
      if (run is not null && BenchOuterPhases.IsTerminal(run.Phase))
      {
        return run;
      }

      await Task.Delay(25);
    }

    throw new TimeoutException($"Run {runId} did not reach a terminal phase within {timeout}.");
  }

  private static async Task WritePresentationFixtureAsync(
    string runDirectory,
    string runId,
    Guid baselineId,
    Guid compareId,
    long baselineInput,
    long baselineOutput,
    long baselineOverhead,
    long compareInput,
    long compareOutput,
    long compareOverhead,
    BenchmarkModelKind? modelKind,
    decimal inputUsdPer1M = 0m,
    decimal outputUsdPer1M = 0m)
  {
    Directory.CreateDirectory(runDirectory);

    object? costRates = modelKind is null
      ? null
      : new
      {
        inputUsdPer1M = inputUsdPer1M,
        outputUsdPer1M = outputUsdPer1M,
        compressionInputUsdPer1M = 0m,
        compressionOutputUsdPer1M = 0m,
        developerUsdPerHour = 0m,
        machineUsdPerHour = 0m,
        modelKind = (int)modelKind.Value
      };

    var presentation = new
    {
      runId,
      costRates,
      metrics = new
      {
        paired = new[]
        {
          new
          {
            name = "fixture-conversation",
            mafCompact = new
            {
              conversationId = baselineId,
              turnCount = 1,
              inputTokens = baselineInput,
              outputTokens = baselineOutput,
              compressionOverheadTokens = baselineOverhead,
              conversationWallClockMs = 1_000
            },
            comprexy = new
            {
              conversationId = compareId,
              turnCount = 1,
              inputTokens = compareInput,
              outputTokens = compareOutput,
              compressionOverheadTokens = compareOverhead,
              conversationWallClockMs = 900
            }
          }
        }
      },
      turnSeriesPaths = new[] { "turns-maf-compact-fixture.json" }
    };

    await File.WriteAllTextAsync(
      Path.Combine(runDirectory, "presentation.json"),
      JsonSerializer.Serialize(presentation, ArtifactJsonOptions));
  }

  private static async Task WaitUntilActiveRunAsync(
    BenchmarkOrchestrationFactory factory,
    string runId,
    TimeSpan timeout)
  {
    var deadline = DateTimeOffset.UtcNow + timeout;
    while (DateTimeOffset.UtcNow < deadline)
    {
      using var scope = factory.Services.CreateScope();
      var orchestrator = scope.ServiceProvider.GetRequiredService<IBenchRunOrchestrator>();
      if (orchestrator.ActiveRunId == runId)
      {
        return;
      }

      await Task.Delay(25);
    }

    throw new TimeoutException($"Run {runId} was not active within {timeout}.");
  }

  private static async Task<BenchmarkRunSummaryDto> WaitForTerminalPhaseAsync(
    HttpClient client,
    string runId,
    TimeSpan timeout)
  {
    var deadline = DateTimeOffset.UtcNow + timeout;
    while (DateTimeOffset.UtcNow < deadline)
    {
      var response = await client.GetAsync($"/v1/comprexy/benchmarks/runs/{runId}");
      response.EnsureSuccessStatusCode();
      var run = await response.Content.ReadFromJsonAsync<BenchmarkRunSummaryDto>();
      if (run is not null && BenchOuterPhases.IsTerminal(run.Phase))
      {
        return run;
      }

      await Task.Delay(25);
    }

    throw new TimeoutException($"Run {runId} did not reach a terminal phase within {timeout}.");
  }

  private static async Task<(Guid BaselineId, Guid CompareId)> SeedPresentationConversationsAsync(
    IServiceProvider services)
  {
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ComprexyDbContext>();
    var baselineId = Guid.NewGuid();
    var compareId = Guid.NewGuid();
    var now = DateTimeOffset.UnixEpoch;

    var baselineSummary = ConversationMetricsSummary.Create(baselineId, now);
    baselineSummary.ApplyTurn(CreateMetricTurn(baselineId, 1, compressedInput: 1_200, completion: 300), now);
    baselineSummary.ApplyCompressionOverhead(50, now);

    var compareSummary = ConversationMetricsSummary.Create(compareId, now);
    compareSummary.ApplyTurn(CreateMetricTurn(compareId, 1, compressedInput: 900, completion: 250), now);
    compareSummary.ApplyCompressionOverhead(30, now);

    db.ConversationMetricsSummaries.AddRange(baselineSummary, compareSummary);
    await db.SaveChangesAsync();
    return (baselineId, compareId);
  }

  private static ConversationTurnMetric CreateMetricTurn(
    Guid conversationId,
    int turnIndex,
    int compressedInput,
    int completion) =>
    ConversationTurnMetric.Create(
      conversationId,
      turnIndex,
      DateTimeOffset.UnixEpoch.AddMinutes(turnIndex),
      "model",
      rawInputTokensEstimated: compressedInput + 100,
      compressedInputTokensEstimated: compressedInput,
      actualPromptTokens: compressedInput,
      actualCompletionTokens: completion,
      softBudgetExceeded: false,
      hardBudgetExceeded: false,
      trimTriggered: false,
      workingMemoryVersionUsed: 1,
      rawMessageCount: 5,
      sentMessageCount: 3,
      requestHash: $"request-{turnIndex}",
      sentPayloadHash: $"sent-{turnIndex}",
      durationMs: 1_000,
      upstreamDurationMs: 700,
      prepareDurationMs: 200,
      DateTimeOffset.UnixEpoch.AddMinutes(turnIndex));

  private sealed class BenchmarkOrchestrationFactory : WebApplicationFactory<Program>
  {
    private readonly bool _preventIndexWrite;

    public BenchmarkOrchestrationFactory(bool preventIndexWrite = false)
    {
      _preventIndexWrite = preventIndexWrite;
      BenchRoot = Path.Combine("/tmp", "fixture-bench", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(BenchRoot);
      DatabasePath = Path.Combine(BenchRoot, "comprexy.db");
      RunsRoot = Path.Combine(BenchRoot, "reports", "bench");
      LockPath = Path.Combine(RunsRoot, ".active-run.lock");
      ProcessRunner = new FakeBenchProcessRunner();

      if (_preventIndexWrite)
      {
        Directory.CreateDirectory(RunsRoot);
        Directory.CreateDirectory(Path.Combine(RunsRoot, "index.json"));
      }
    }

    public FakeBenchProcessRunner ProcessRunner { get; }

    public string BenchRoot { get; }

    public string DatabasePath { get; }

    public string RunsRoot { get; }

    public string LockPath { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
      builder.ConfigureAppConfiguration((_, configuration) =>
      {
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
          ["ConnectionStrings:Comprexy"] = $"Data Source={DatabasePath}",
          ["BenchOrchestration:RepoRoot"] = BenchRoot,
          ["BenchOrchestration:RunsRootRelative"] = "reports/bench",
          ["BenchOrchestration:Enabled"] = "true",
          ["BenchOrchestration:AllowSpawn"] = "true",
          ["BenchOrchestration:SmokeConversationTimeoutSeconds"] = "1200",
          ["Auth:RequiredApiKey"] = string.Empty
        });
      });

      builder.ConfigureTestServices(services =>
      {
        services.AddSingleton<IBenchProcessRunner>(ProcessRunner);
      });
    }

    public override async ValueTask DisposeAsync()
    {
      await base.DisposeAsync();
      try
      {
        if (Directory.Exists(BenchRoot))
        {
          Directory.Delete(BenchRoot, recursive: true);
        }
      }
      catch (IOException)
      {
        // Best-effort cleanup for temp fixture paths.
      }
    }
  }

  private sealed class FakeBenchProcessRunner : IBenchProcessRunner
  {
    private readonly TaskCompletionSource _runCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _runStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _runExitCode;
    private int _reportExitCode;

    public bool BlockRun { get; set; } = true;

    public int RunCallCount { get; private set; }

    public int ReportCallCount { get; private set; }

    public CancellationToken? LastCancellationToken { get; private set; }

    public IReadOnlyList<string>? LastRunArguments { get; private set; }

    public Task RunStarted => _runStarted.Task;

    public void Configure(int runExitCode = 0, int reportExitCode = 0)
    {
      _runExitCode = runExitCode;
      _reportExitCode = reportExitCode;
    }

    public void CompleteRun()
    {
      _runCompletion.TrySetResult();
    }

    public async Task<BenchProcessResult> RunAsync(
      string workingDirectory,
      IReadOnlyList<string> arguments,
      CancellationToken cancellationToken)
    {
      LastCancellationToken = cancellationToken;
      var isReport = arguments.Any(argument => string.Equals(argument, "report", StringComparison.Ordinal));
      if (isReport)
      {
        ReportCallCount++;
        return new BenchProcessResult(
          _reportExitCode,
          _reportExitCode != 0 ? "report failed" : null);
      }

      RunCallCount++;
      LastRunArguments = arguments.ToList();
      _runStarted.TrySetResult();
      if (BlockRun)
      {
        try
        {
          await _runCompletion.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
          throw;
        }
      }

      return new BenchProcessResult(
        _runExitCode,
        _runExitCode != 0 ? "run failed" : null);
    }
  }
}
