using Microsoft.AspNetCore.Http;

namespace Comprexy.Infrastructure.Providers;

/// <summary>
/// Forwards unsupported OpenAI-compatible <c>/v1/*</c> requests to the configured upstream provider
/// without context rebuild or compression (true reverse-proxy passthrough).
/// </summary>
public interface IUpstreamPassthroughProxy
{
    Task ForwardAsync(HttpContext context, CancellationToken cancellationToken);
}
