using Comprexy.Application.Models.Benchmarking;
using Comprexy.ControlApi.Configuration;
using Comprexy.ControlApi.Contracts.Benchmark;
using Microsoft.Extensions.Options;

namespace Comprexy.ControlApi.Benchmarking;

public interface IBenchRunOrchestrator
{
    string? ActiveRunId { get; }

    Task<BenchmarkStartRunResponse> StartAsync(
        BenchmarkStartRunRequest request,
        CancellationToken cancellationToken);

    Task<bool> CancelAsync(string runId, CancellationToken cancellationToken);

    Task<int> ReportAsync(string runId, BenchmarkCostRates? rates, CancellationToken cancellationToken);

    Task<IReadOnlyList<BenchmarkRunSummaryDto>> ListRunsAsync(CancellationToken cancellationToken);

    Task<BenchmarkRunSummaryDto?> GetRunAsync(string runId, CancellationToken cancellationToken);

    Task<BenchmarkRunArtifactsDto?> GetArtifactsAsync(string runId, CancellationToken cancellationToken);
}

public sealed class BenchRunOrchestrator : IBenchRunOrchestrator
{
    private readonly BenchOrchestrationOptions _options;
    private readonly IBenchProcessRunner _processRunner;
    private readonly ILogger<BenchRunOrchestrator> _logger;
    private readonly string _repoRoot;
    private readonly string _runsRoot;
    private readonly string _lockPath;
    private readonly string _indexPath;

    private readonly object _gate = new();
    private BenchRunLock? _lock;
    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private string? _activeRunId;

    public BenchRunOrchestrator(
        IOptions<BenchOrchestrationOptions> options,
        IBenchProcessRunner processRunner,
        IHostEnvironment hostEnvironment,
        ILogger<BenchRunOrchestrator> logger)
    {
        _options = options.Value;
        _processRunner = processRunner;
        _logger = logger;
        _repoRoot = ResolveRepoRoot(hostEnvironment, _options.RepoRoot);
        _runsRoot = Path.Combine(_repoRoot, _options.RunsRootRelative);
        _lockPath = Path.Combine(_runsRoot, _options.LockFileName);
        _indexPath = Path.Combine(_runsRoot, "index.json");
    }

    public string? ActiveRunId
    {
        get
        {
            lock (_gate)
            {
                return _activeRunId;
            }
        }
    }

    public async Task<BenchmarkStartRunResponse> StartAsync(
        BenchmarkStartRunRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            throw new InvalidOperationException("Bench orchestration is disabled.");
        }

        if (!_options.AllowSpawn)
        {
            throw new InvalidOperationException("Bench orchestration spawn is disabled.");
        }

        lock (_gate)
        {
            if (_activeRunId is not null)
            {
                throw new BenchConflictException(_activeRunId);
            }
        }

        var runId = BuildRunId(request.RunLabel);
        var runDirectory = Path.Combine(_runsRoot, runId);
        var rates = (request.Rates ?? BenchmarkCostRates.LocalDefaults()) with
        {
            ModelKind = request.ModelKind
        };
        rates = rates.WithCompressionDefaultsFromMain();

        var acquiredLock = new BenchRunLock();
        if (!acquiredLock.TryAcquire(_lockPath, runId, out var existing))
        {
            await acquiredLock.DisposeAsync();
            throw new BenchConflictException(existing?.RunId ?? "unknown");
        }

        try
        {
            Directory.CreateDirectory(runDirectory);
            var statusPath = Path.Combine(runDirectory, "status.json");
            await BenchArtifactStore.WriteStatusAsync(
                statusPath,
                new BenchStatusDocument
                {
                    RunId = runId,
                    Phase = BenchOuterPhases.Queued,
                    StartedAt = DateTimeOffset.UtcNow,
                    ConversationNames = request.Conversations.ToList()
                },
                cancellationToken);

            await UpsertIndexAsync(runId, BenchOuterPhases.Queued, request.Conversations, rates.ModelKind, cancellationToken);

            var cts = new CancellationTokenSource();
            lock (_gate)
            {
                _lock = acquiredLock;
                _runCts = cts;
                _activeRunId = runId;
                _runTask = Task.Run(() => RunJobAsync(runId, runDirectory, request, rates, cts.Token), CancellationToken.None);
            }

            return new BenchmarkStartRunResponse { RunId = runId };
        }
        catch
        {
            acquiredLock.Release();
            await acquiredLock.DisposeAsync();
            throw;
        }
    }

    public async Task<bool> CancelAsync(string runId, CancellationToken cancellationToken)
    {
        CancellationTokenSource? cts;
        Task? runTask;
        lock (_gate)
        {
            if (_activeRunId != runId || _runCts is null)
            {
                return false;
            }

            cts = _runCts;
            runTask = _runTask;
        }

        await cts.CancelAsync();
        if (runTask is not null)
        {
            await runTask.WaitAsync(cancellationToken);
        }

        return true;
    }

    public async Task<int> ReportAsync(string runId, BenchmarkCostRates? rates, CancellationToken cancellationToken)
    {
        if (ActiveRunId is not null && ActiveRunId != runId)
        {
            throw new BenchConflictException(ActiveRunId);
        }

        var runDirectory = Path.Combine(_runsRoot, runId);
        if (!Directory.Exists(runDirectory))
        {
            return -1;
        }

        var resolvedRates = (rates ?? BenchmarkCostRates.LocalDefaults()).WithCompressionDefaultsFromMain();
        var args = BuildHarnessArgs("report", runId, resolvedRates, conversations: []);
        var result = await _processRunner.RunAsync(_repoRoot, args, cancellationToken);
        return result.ExitCode;
    }

    public async Task<IReadOnlyList<BenchmarkRunSummaryDto>> ListRunsAsync(CancellationToken cancellationToken)
    {
        var summaries = new Dictionary<string, BenchmarkRunSummaryDto>(StringComparer.Ordinal);
        var index = await BenchArtifactStore.ReadOrCreateIndexAsync(_indexPath, cancellationToken);
        foreach (var entry in index.Runs)
        {
            summaries[entry.RunId] = await MapIndexEntryAsync(entry, cancellationToken);
        }

        if (!Directory.Exists(_runsRoot))
        {
            return summaries.Values.OrderByDescending(s => s.StartedAt).ToList();
        }

        foreach (var directory in Directory.EnumerateDirectories(_runsRoot))
        {
            var runId = Path.GetFileName(directory);
            if (summaries.ContainsKey(runId))
            {
                continue;
            }

            var manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            summaries[runId] = await MapFromManifestAsync(runId, directory, cancellationToken);
        }

        return summaries.Values.OrderByDescending(s => s.StartedAt ?? DateTimeOffset.MinValue).ToList();
    }

    public async Task<BenchmarkRunSummaryDto?> GetRunAsync(string runId, CancellationToken cancellationToken)
    {
        var runDirectory = Path.Combine(_runsRoot, runId);
        var statusPath = Path.Combine(runDirectory, "status.json");
        var status = await BenchArtifactStore.ReadStatusAsync(statusPath, cancellationToken);
        if (status is not null)
        {
            return MapStatus(runId, status);
        }

        if (!Directory.Exists(runDirectory))
        {
            return null;
        }

        return await MapFromManifestAsync(runId, runDirectory, cancellationToken);
    }

    public Task<BenchmarkRunArtifactsDto?> GetArtifactsAsync(string runId, CancellationToken cancellationToken)
    {
        var runDirectory = Path.Combine(_runsRoot, runId);
        if (!Directory.Exists(runDirectory))
        {
            return Task.FromResult<BenchmarkRunArtifactsDto?>(null);
        }

        var turnSeries = Directory.EnumerateFiles(runDirectory, "turns-*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(Path.GetFileName)
            .Where(p => p is not null)
            .Cast<string>()
            .ToList();

        return Task.FromResult<BenchmarkRunArtifactsDto?>(new BenchmarkRunArtifactsDto
        {
            RunId = runId,
            ManifestPath = File.Exists(Path.Combine(runDirectory, "manifest.json")) ? "manifest.json" : null,
            MetricsPath = File.Exists(Path.Combine(runDirectory, "metrics.json")) ? "metrics.json" : null,
            SummaryPath = File.Exists(Path.Combine(runDirectory, "summary.md")) ? "summary.md" : null,
            PresentationPath = File.Exists(Path.Combine(runDirectory, "presentation.json")) ? "presentation.json" : null,
            TurnSeriesPaths = turnSeries
        });
    }

    private async Task RunJobAsync(
        string runId,
        string runDirectory,
        BenchmarkStartRunRequest request,
        BenchmarkCostRates rates,
        CancellationToken cancellationToken)
    {
        var statusPath = Path.Combine(runDirectory, "status.json");
        try
        {
            await SetPhaseAsync(statusPath, runId, BenchOuterPhases.Starting, cancellationToken);
            await SetPhaseAsync(statusPath, runId, BenchOuterPhases.Running, cancellationToken);

            var runArgs = BuildHarnessArgs("run", runId, rates, request.Conversations, exactRunId: true);
            var runResult = await _processRunner.RunAsync(_repoRoot, runArgs, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                await SetTerminalAsync(
                    statusPath,
                    runId,
                    BenchOuterPhases.Cancelled,
                    "Cancelled by operator.",
                    cancellationToken);
                return;
            }

            if (runResult.ExitCode != 0)
            {
                await SetTerminalAsync(
                    statusPath,
                    runId,
                    BenchOuterPhases.Failed,
                    runResult.StandardError ?? $"bench run exited {runResult.ExitCode}",
                    cancellationToken);
                return;
            }

            await SetPhaseAsync(statusPath, runId, BenchOuterPhases.Reporting, cancellationToken);
            var reportArgs = BuildHarnessArgs("report", runId, rates, request.Conversations, exactRunId: true);
            var reportResult = await _processRunner.RunAsync(_repoRoot, reportArgs, cancellationToken);

            if (reportResult.ExitCode != 0)
            {
                await SetTerminalAsync(
                    statusPath,
                    runId,
                    BenchOuterPhases.CompletedWithReportError,
                    reportResult.StandardError ?? $"bench report exited {reportResult.ExitCode}",
                    cancellationToken);
                return;
            }

            await SetTerminalAsync(statusPath, runId, BenchOuterPhases.Completed, lastError: null, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await SetTerminalAsync(
                statusPath,
                runId,
                BenchOuterPhases.Cancelled,
                "Cancelled by operator.",
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bench run {RunId} failed in orchestrator job.", runId);
            await SetTerminalAsync(
                statusPath,
                runId,
                BenchOuterPhases.Failed,
                ex.Message,
                CancellationToken.None);
        }
        finally
        {
            lock (_gate)
            {
                _lock?.Release();
                _lock = null;
                _runCts?.Dispose();
                _runCts = null;
                _activeRunId = null;
                _runTask = null;
            }
        }
    }

    private async Task SetPhaseAsync(
        string statusPath,
        string runId,
        string phase,
        CancellationToken cancellationToken)
    {
        var status = await BenchArtifactStore.ReadStatusAsync(statusPath, cancellationToken)
            ?? new BenchStatusDocument { RunId = runId, StartedAt = DateTimeOffset.UtcNow };
        status.Phase = phase;
        await BenchArtifactStore.WriteStatusAsync(statusPath, status, cancellationToken);
        await UpsertIndexAsync(runId, phase, status.ConversationNames?.ToList() ?? [], null, cancellationToken);
    }

    private async Task SetTerminalAsync(
        string statusPath,
        string runId,
        string phase,
        string? lastError,
        CancellationToken cancellationToken)
    {
        var status = await BenchArtifactStore.ReadStatusAsync(statusPath, cancellationToken)
            ?? new BenchStatusDocument { RunId = runId, StartedAt = DateTimeOffset.UtcNow };
        status.Phase = phase;
        status.LastError = lastError;
        await BenchArtifactStore.WriteStatusAsync(statusPath, status, cancellationToken);
        await UpsertIndexAsync(runId, phase, status.ConversationNames?.ToList() ?? [], null, cancellationToken);
    }

    private async Task UpsertIndexAsync(
        string runId,
        string phase,
        IReadOnlyList<string> conversationNames,
        BenchmarkModelKind? modelKind,
        CancellationToken cancellationToken)
    {
        var index = await BenchArtifactStore.ReadOrCreateIndexAsync(_indexPath, cancellationToken);
        var entry = index.Runs.FirstOrDefault(r => r.RunId == runId);
        if (entry is null)
        {
            entry = new BenchIndexEntry
            {
                RunId = runId,
                Phase = phase,
                StartedAt = DateTimeOffset.UtcNow
            };
            index.Runs.Add(entry);
        }

        entry.Phase = phase;
        entry.UpdatedAt = DateTimeOffset.UtcNow;
        if (conversationNames.Count > 0)
        {
            entry.ConversationNames = conversationNames.ToList();
        }

        if (modelKind is not null)
        {
            entry.ModelKind = modelKind.Value.ToString();
        }

        await BenchArtifactStore.WriteIndexAsync(_indexPath, index, cancellationToken);
    }

    private static string BuildRunId(string? label)
    {
        var stamp = DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(label) ? stamp : $"{stamp}-{label.Trim()}";
    }

    private IReadOnlyList<string> BuildHarnessArgs(
        string command,
        string runId,
        BenchmarkCostRates rates,
        IReadOnlyList<string> conversations,
        bool exactRunId = false)
    {
        var projectPath = Path.Combine(_repoRoot, _options.HarnessProjectPath);
        var conversationTimeoutSeconds = BenchmarkScenarioParser.IsSmokeOnlyRun(conversations)
            ? _options.SmokeConversationTimeoutSeconds
            : _options.ConversationTimeoutSeconds;

        var args = new List<string>
        {
            "run",
            "--project",
            projectPath,
            "--",
            command,
            "--run-id",
            runId,
            "--db",
            Path.Combine(_repoRoot, _options.DatabasePathRelative),
            "--proxy-port-maf-compact",
            _options.MafCompactPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--proxy-port-comprexy",
            _options.ComprexyPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--control-api-port",
            _options.ControlApiPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--completion-timeout",
            _options.CompletionTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--conversation-timeout",
            conversationTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        if (BenchmarkScenarioParser.IsSmokeOnlyRun(conversations))
        {
            args.Add("--continue-past-baseline-failure");
        }

        if (exactRunId)
        {
            args.Add("--exact-run-id");
        }

        foreach (var conversation in conversations)
        {
            args.Add("--conversation");
            args.Add(conversation);
        }

        var ratesPath = Path.Combine(_runsRoot, runId, "cost-rates.json");
        Directory.CreateDirectory(Path.GetDirectoryName(ratesPath)!);
        File.WriteAllText(
            ratesPath,
            System.Text.Json.JsonSerializer.Serialize(
                rates,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
                {
                    WriteIndented = true
                }));
        args.Add("--cost-rates");
        args.Add(ratesPath);

        return args;
    }

    private async Task<BenchmarkRunSummaryDto> MapIndexEntryAsync(
        BenchIndexEntry entry,
        CancellationToken cancellationToken)
    {
        var runDirectory = Path.Combine(_runsRoot, entry.RunId);
        var status = await BenchArtifactStore.ReadStatusAsync(Path.Combine(runDirectory, "status.json"), cancellationToken);
        if (status is not null)
        {
            return MapStatus(entry.RunId, status) with
            {
                ConversationNames = entry.ConversationNames.Count > 0 ? entry.ConversationNames : status.ConversationNames?.ToList() ?? []
            };
        }

        return new BenchmarkRunSummaryDto
        {
            RunId = entry.RunId,
            Phase = entry.Phase,
            StartedAt = entry.StartedAt,
            UpdatedAt = entry.UpdatedAt,
            ConversationNames = entry.ConversationNames
        };
    }

    private async Task<BenchmarkRunSummaryDto> MapFromManifestAsync(
        string runId,
        string runDirectory,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(runDirectory, "manifest.json");
        await using var stream = File.OpenRead(manifestPath);
        using var document = await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var startedAt = root.TryGetProperty("startedAt", out var started) && started.TryGetDateTimeOffset(out var startedValue)
            ? startedValue
            : (DateTimeOffset?)null;
        var conversations = new List<string>();
        if (root.TryGetProperty("arms", out var arms) && arms.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var arm in arms.EnumerateArray())
            {
                if (!arm.TryGetProperty("conversations", out var convs) || convs.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var conv in convs.EnumerateArray())
                {
                    if (conv.TryGetProperty("name", out var name))
                    {
                        var value = name.GetString();
                        if (!string.IsNullOrWhiteSpace(value) && !conversations.Contains(value, StringComparer.Ordinal))
                        {
                            conversations.Add(value);
                        }
                    }
                }
            }
        }

        return new BenchmarkRunSummaryDto
        {
            RunId = runId,
            Phase = BenchOuterPhases.Completed,
            StartedAt = startedAt,
            ConversationNames = conversations
        };
    }

    private static BenchmarkRunSummaryDto MapStatus(string runId, BenchStatusDocument status) =>
        new()
        {
            RunId = runId,
            Phase = status.Phase ?? BenchOuterPhases.Queued,
            RunPhase = status.RunPhase,
            StartedAt = status.StartedAt,
            UpdatedAt = status.UpdatedAt,
            LastError = status.LastError,
            Arm = status.Arm,
            ConversationName = status.ConversationName,
            PromptsCompleted = status.PromptsCompleted,
            PromptCount = status.PromptCount
        };

    private static string ResolveRepoRoot(IHostEnvironment hostEnvironment, string? overrideRoot)
    {
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return Path.GetFullPath(overrideRoot);
        }

        var directory = new DirectoryInfo(hostEnvironment.ContentRootPath);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Comprexy.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Path.GetFullPath(Path.Combine(hostEnvironment.ContentRootPath, "..", ".."));
    }
}

public sealed class BenchConflictException : Exception
{
    public BenchConflictException(string activeRunId)
        : base($"Another bench run is active: {activeRunId}.")
    {
        ActiveRunId = activeRunId;
    }

    public string ActiveRunId { get; }
}
