using System.Text.Json;
using System.Text.Json.Serialization;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services.ToolIr;

/// <summary>
/// Idle, preemptible background worker that proposes result-shape descriptors from sanitized features.
/// </summary>
public sealed class ToolIrShapeLearnerService : BackgroundService
{
    private static readonly JsonSerializerOptions PromptJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly IToolIrShapeLearnQueue _queue;
    private readonly IUpstreamActivityGate _gate;
    private readonly ToolIrResultShapeStore _store;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ProviderEndpointResolver _endpointResolver;
    private readonly IOptions<ToolSchemaOptions> _options;
    private readonly ILogger<ToolIrShapeLearnerService> _logger;

    public ToolIrShapeLearnerService(
        IToolIrShapeLearnQueue queue,
        IUpstreamActivityGate gate,
        ToolIrResultShapeStore store,
        IServiceScopeFactory scopeFactory,
        ProviderEndpointResolver endpointResolver,
        IOptions<ToolSchemaOptions> options,
        ILogger<ToolIrShapeLearnerService> logger)
    {
        _queue = queue;
        _gate = gate;
        _store = store;
        _scopeFactory = scopeFactory;
        _endpointResolver = endpointResolver;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.ReadAllAsync(stoppingToken))
        {
            await ProcessJobAsync(job, stoppingToken);
        }
    }

    /// <summary>
    /// One dequeue cycle. Exposed for unit tests so idle/preempt proofs use a fake gate
    /// and <see cref="TaskCompletionSource"/> — no hosted loop or wall-clock waits.
    /// </summary>
    internal async Task ProcessJobAsync(ToolIrShapeLearnJob job, CancellationToken stoppingToken)
    {
        var key = (job.ConversationId, job.ClientToolName);
        var promoted = false;
        try
        {
            await _gate.WaitForIdleAsync(
                _options.Value.ResultShape.Learner.IdleDebounce,
                stoppingToken);

            using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken,
                _gate.PreemptToken);

            using var scope = _scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<IChatCompletionClient>();
            var endpoint = _endpointResolver.ResolveCompression();

            var prompt = BuildPrompt(job);
            var request = new UpstreamRequest(
                Messages:
                [
                    new ChatMessage(MessageRole.System, "You select a closed result-shape descriptor. Reply with JSON only."),
                    new ChatMessage(MessageRole.User, prompt)
                ],
                Stream: false,
                Purpose: UpstreamRequestPurpose.ShapeLearner);

            var result = await client.CompleteAsync(endpoint, request, jobCts.Token);
            var reply = result.Content ?? string.Empty;

            if (ToolIrShapeProposalValidator.Validate(
                    ExtractJsonObject(reply),
                    job.Samples,
                    _options.Value.ResultShape,
                    out var descriptor,
                    out var reason) &&
                descriptor is not null)
            {
                _store.Promote(key, descriptor);
                promoted = true;
                _logger.LogDebug(
                    "Shape learner promoted {ClientTool} for {ConversationId}",
                    job.ClientToolName,
                    job.ConversationId);
            }
            else
            {
                _logger.LogDebug(
                    "Shape learner rejected {ClientTool} for {ConversationId}: {Reason}",
                    job.ClientToolName,
                    job.ConversationId,
                    reason);
            }
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Shape learner job preempted for {ClientTool} / {ConversationId}",
                job.ClientToolName,
                job.ConversationId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Shape learner failed for {ClientTool} / {ConversationId}",
                job.ClientToolName,
                job.ConversationId);
        }
        finally
        {
            _store.CompleteJob(key, promoted);
        }
    }

    private static string BuildPrompt(ToolIrShapeLearnJob job)
    {
        var featuresJson = JsonSerializer.Serialize(job.Samples, PromptJsonOptions);
        return
            "Client tool: " + job.ClientToolName + "\n" +
            "Virtual tool: " + job.VirtualToolName + "\n" +
            "Choose one closed descriptor:\n" +
            "{\"envelope\":\"tagged_content\"|\"json_field\"|\"plain\",\"json_field\":\"contents\"|\"content\"|\"text\"|\"data\"|\"result\"|null,\"line_prefix\":\"colon\"|\"pipe\"|\"none\"}\n" +
            "Sanitized structural features (no payload text):\n" +
            featuresJson;
    }

    private static string ExtractJsonObject(string reply)
    {
        var start = reply.IndexOf('{');
        var end = reply.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return reply.Trim();
        }

        return reply[start..(end + 1)];
    }
}
