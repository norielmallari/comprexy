using Comprexy.Application.Abstractions;
using Comprexy.Application.Models;

namespace Comprexy.Application.Services;

/// <summary>
/// Decorator that meters non-learner upstream calls for the idle shape learner busy/preempt gate.
/// </summary>
public sealed class UpstreamActivityTrackingChatCompletionClient : IChatCompletionClient
{
    private readonly IChatCompletionClient _inner;
    private readonly IUpstreamActivityGate _gate;

    public UpstreamActivityTrackingChatCompletionClient(
        IChatCompletionClient inner,
        IUpstreamActivityGate gate)
    {
        _inner = inner;
        _gate = gate;
    }

    public async Task<UpstreamChatResult> CompleteAsync(
        ProviderEndpoint endpoint,
        UpstreamRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Purpose == UpstreamRequestPurpose.ShapeLearner)
        {
            return await _inner.CompleteAsync(endpoint, request, cancellationToken);
        }

        using var lease = _gate.BeginClientDrivenCall();
        return await _inner.CompleteAsync(endpoint, request, cancellationToken);
    }

    public async Task<UpstreamChatResult> StreamAsync(
        ProviderEndpoint endpoint,
        UpstreamRequest request,
        Func<string, CancellationToken, Task> onRawSseData,
        CancellationToken cancellationToken)
    {
        if (request.Purpose == UpstreamRequestPurpose.ShapeLearner)
        {
            return await _inner.StreamAsync(endpoint, request, onRawSseData, cancellationToken);
        }

        using var lease = _gate.BeginClientDrivenCall();
        return await _inner.StreamAsync(endpoint, request, onRawSseData, cancellationToken);
    }
}
