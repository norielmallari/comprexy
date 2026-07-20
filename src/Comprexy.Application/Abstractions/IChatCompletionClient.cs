using Comprexy.Application.Models;

namespace Comprexy.Application.Abstractions;

/// <summary>
/// Calls an OpenAI-compatible chat completions endpoint. The same implementation is used for
/// both proxying client requests upstream and running LLM-based compression.
/// </summary>
public interface IChatCompletionClient
{
    Task<UpstreamChatResult> CompleteAsync(
        ProviderEndpoint endpoint,
        UpstreamRequest request,
        CancellationToken cancellationToken);

    Task<UpstreamChatResult> StreamAsync(
        ProviderEndpoint endpoint,
        UpstreamRequest request,
        Func<string, CancellationToken, Task> onRawSseData,
        CancellationToken cancellationToken);
}
