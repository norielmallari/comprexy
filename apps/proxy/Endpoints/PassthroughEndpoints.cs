using Comprexy.Application.Abstractions;
using Comprexy.Infrastructure.Hosting;
using Comprexy.Infrastructure.Providers;

namespace Comprexy.Api.Endpoints;

public static class PassthroughEndpoints
{
    public static IEndpointRouteBuilder MapPassthroughEndpoints(this IEndpointRouteBuilder app)
    {
        // Unsupported OpenAI-compatible routes: reverse-proxy to Provider (chat completions handled above).
        app.Map("/v1/{**path}", async (
            HttpContext httpContext,
            IUpstreamPassthroughProxy passthrough,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("Comprexy.Api.Passthrough");
            try
            {
                await passthrough.ForwardAsync(httpContext, cancellationToken);
                return Results.Empty;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Passthrough proxy failed for {Method} {Path}",
                    httpContext.Request.Method,
                    httpContext.Request.Path);
                if (httpContext.Response.HasStarted)
                {
                    return Results.Empty;
                }

                return Results.Json(
                    new ErrorResponseDto
                    {
                        Error = new ErrorDetailDto { Message = "Upstream provider error.", Type = "upstream_error" }
                    },
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });

        return app;
    }
}
