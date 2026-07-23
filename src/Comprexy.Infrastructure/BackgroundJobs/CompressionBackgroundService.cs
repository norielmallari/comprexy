using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comprexy.Infrastructure.BackgroundJobs;

/// <summary>
/// Drains the compression queue and runs each job in its own DI scope.
/// Soft compression lease kind follows <see cref="ContextPolicyOptions.CancelBackgroundCompressionOnChat"/>:
/// preemptible (cancel on chat) when true, exclusive (chat waits) when false.
/// </summary>
public class CompressionBackgroundService : BackgroundService
{
    private readonly ICompressionQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ContextPolicyOptions _policy;
    private readonly ILogger<CompressionBackgroundService> _logger;

    public CompressionBackgroundService(
        ICompressionQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<ContextPolicyOptions> policy,
        ILogger<CompressionBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _policy = policy.Value;
        _logger = logger;
    }

    /// <summary>
    /// Soft compression lease: preemptible when cancel-on-chat is enabled, otherwise exclusive.
    /// </summary>
    public static ConversationGateLeaseKind ResolveSoftCompressionLeaseKind(bool cancelOnChat) =>
        cancelOnChat
            ? ConversationGateLeaseKind.Preemptible
            : ConversationGateLeaseKind.Exclusive;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var conversations = scope.ServiceProvider.GetRequiredService<IConversationRepository>();
                var gate = scope.ServiceProvider.GetRequiredService<IConversationRequestGate>();
                var orchestrator = scope.ServiceProvider.GetRequiredService<ICompressionOrchestrator>();
                var requestTraceFiles = scope.ServiceProvider.GetRequiredService<IRequestTraceFileSession>();

                var conversation = await conversations.FindByIdAsync(job.ConversationId, stoppingToken);
                if (conversation is null)
                {
                    _logger.LogWarning(
                        "compression job skipped: conversation {ConversationId} not found.",
                        job.ConversationId);
                    continue;
                }

                var leaseKind = ResolveSoftCompressionLeaseKind(_policy.CancelBackgroundCompressionOnChat);
                await using var lease = await gate.AcquireAsync(
                    conversation.ConversationKey,
                    leaseKind,
                    stoppingToken);

                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    stoppingToken,
                    lease.Token);
                using var compressionTrace = requestTraceFiles.BeginCompression(job.ConversationId, job.Mode.ToString());
                _logger.LogInformation(
                    "Running compression job for conversation {ConversationId} mode={Mode}.",
                    job.ConversationId,
                    job.Mode);
                await orchestrator.RunAsync(
                    job.ConversationId,
                    job.Mode,
                    linked.Token,
                    job.PreferredModel);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation(
                    "compression_cancelled conversationId={ConversationId} mode={Mode} reason=client_preempt",
                    job.ConversationId,
                    job.Mode);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unhandled error running compression job for conversation {ConversationId}.",
                    job.ConversationId);
            }
        }
    }
}
