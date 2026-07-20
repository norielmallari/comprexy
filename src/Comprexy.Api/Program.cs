using Comprexy.Api.Contracts;
using Comprexy.Api.Mapping;
using Comprexy.Api.Middleware;
using Comprexy.Api.Streaming;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.DependencyInjection;
using Comprexy.Application.Services;
using Comprexy.Application.Tracing;
using Comprexy.Infrastructure.DependencyInjection;
using Comprexy.Infrastructure.Persistence;
using Comprexy.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Optional machine-specific overrides (gitignored). Copy from appsettings.Local.json.example.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddComprexyApplication(builder.Configuration);
builder.Services.AddComprexyInfrastructure(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ComprexyDbContext>();

    if (HasClearDatabaseArg(args))
    {
        var clearLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Comprexy");
        await DatabaseClearer.RebuildAsync(dbContext);
        clearLogger.LogInformation("Comprexy database rebuilt from migrations. Exiting.");
        return;
    }

    dbContext.Database.Migrate();
}

app.UseMiddleware<ApiKeyAuthMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapGet("/", () => Results.Ok(new { status = "ok", service = "comprexy" }));
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapPost("/v1/chat/completions", async (
    HttpContext httpContext,
    ProxyChatCompletionService proxyService,
    IPayloadTraceLogger payloadTrace,
    IRequestTraceFileSession requestTraceFiles,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    using var requestTrace = requestTraceFiles.Begin(httpContext.TraceIdentifier);

    JsonElement rawRequest;
    try
    {
        using var document = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: cancellationToken);
        rawRequest = document.RootElement.Clone();
    }
    catch (JsonException)
    {
        return Results.Json(
            new ErrorResponseDto
            {
                Error = new ErrorDetailDto { Message = "Request body must be valid JSON.", Type = "invalid_request_error" }
            },
            statusCode: StatusCodes.Status400BadRequest);
    }

    try
    {
        payloadTrace.LogInput(PayloadTraceLabels.ClientInput, rawRequest.GetRawText());

        var conversationIdHeader = httpContext.Request.Headers[Comprexy.Api.ComprexyHeaders.ConversationId].FirstOrDefault();
        var incomingRequest = ChatCompletionRequestParser.Parse(rawRequest, conversationIdHeader);

        if (incomingRequest.Stream)
        {
            await StreamChatCompletionAsync(httpContext, proxyService, payloadTrace, logger, incomingRequest, cancellationToken);
            return Results.Empty;
        }

        var result = await proxyService.HandleAsync(incomingRequest, cancellationToken);
        httpContext.Response.Headers[Comprexy.Api.ComprexyHeaders.ConversationId] = result.ConversationId.ToString();

        if (!string.IsNullOrWhiteSpace(result.RawResponseJson))
        {
            payloadTrace.LogOutput(PayloadTraceLabels.ClientOutput, result.RawResponseJson);
            return Results.Content(result.RawResponseJson, "application/json", Encoding.UTF8);
        }

        var responseDto = ChatCompletionMapper.ToResponseDto(result);
        payloadTrace.LogOutput(PayloadTraceLabels.ClientOutputReassembled, responseDto);
        return Results.Ok(responseDto);
    }
    catch (ArgumentException ex)
    {
        return Results.Json(
            new ErrorResponseDto { Error = new ErrorDetailDto { Message = ex.Message, Type = "invalid_request_error" } },
            statusCode: StatusCodes.Status400BadRequest);
    }
    catch (Comprexy.Application.Exceptions.ContextBudgetExceededException ex)
    {
        return Results.Json(
            new ErrorResponseDto
            {
                Error = new ErrorDetailDto { Message = ex.Message, Type = "context_length_exceeded" }
            },
            statusCode: StatusCodes.Status413PayloadTooLarge);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        return Results.Empty;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unhandled error while proxying chat completion request.");
        if (httpContext.Response.HasStarted)
        {
            return Results.Empty;
        }

        return Results.Json(
            new ErrorResponseDto { Error = new ErrorDetailDto { Message = "Upstream provider error.", Type = "upstream_error" } },
            statusCode: StatusCodes.Status502BadGateway);
    }
});

// Unsupported OpenAI-compatible routes: reverse-proxy to Provider (chat completions handled above).
app.Map("/v1/{**path}", async (
    HttpContext httpContext,
    IUpstreamPassthroughProxy passthrough,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
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
        logger.LogError(ex, "Passthrough proxy failed for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        if (httpContext.Response.HasStarted)
        {
            return Results.Empty;
        }

        return Results.Json(
            new ErrorResponseDto { Error = new ErrorDetailDto { Message = "Upstream provider error.", Type = "upstream_error" } },
            statusCode: StatusCodes.Status502BadGateway);
    }
});

var providerOptions = builder.Configuration.GetSection(ProviderOptions.SectionName).Get<ProviderOptions>() ?? new ProviderOptions();
var proxyOptions = builder.Configuration.GetSection(ProxyOptions.SectionName).Get<ProxyOptions>() ?? new ProxyOptions();
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
app.Lifetime.ApplicationStarted.Register(() =>
{
    var address = app.Urls.FirstOrDefault() ?? "http://localhost:8129";
    startupLogger.LogInformation(
        """
        Comprexy is running.

        Proxy endpoint:
          {ProxyEndpoint}

        Other /v1 routes:
          reverse-proxied to upstream (passthrough)

        Configured upstream:
          {UpstreamBaseUrl} (model: {UpstreamModel})

        Chat pass-through mode:
          {PassThrough}

        Configure your OpenAI-compatible client to use:
          Base URL: {BaseUrl}
          API Key: any value, or omit (unless Auth:RequiredApiKey is configured)
        """,
        $"{address}/v1/chat/completions",
        providerOptions.BaseUrl,
        providerOptions.Model,
        proxyOptions.PassThrough ? "enabled (raw field-preserving chat proxy)" : "disabled",
        $"{address}/v1");
});

app.Run();

static async Task StreamChatCompletionAsync(
    HttpContext httpContext,
    ProxyChatCompletionService proxyService,
    IPayloadTraceLogger payloadTrace,
    ILogger logger,
    Comprexy.Application.Models.IncomingChatRequest request,
    CancellationToken cancellationToken)
{
    var response = httpContext.Response;
    var streamStarted = false;

    try
    {
        // Defer SSE headers until prepare succeeds so context-budget failures can still return JSON 413.
        var result = await proxyService.HandleStreamingAsync(
            request,
            conversationId =>
            {
                streamStarted = true;
                response.StatusCode = StatusCodes.Status200OK;
                response.ContentType = "text/event-stream";
                response.Headers.CacheControl = "no-cache";
                response.Headers.Connection = "keep-alive";
                response.Headers["X-Accel-Buffering"] = "no";
                response.Headers[Comprexy.Api.ComprexyHeaders.ConversationId] = conversationId.ToString();
            },
            async (data, token) =>
            {
                var sseLine = $"data: {data}\n\n";
                await response.WriteAsync(sseLine, token);
                await response.Body.FlushAsync(token);
            },
            cancellationToken);

        payloadTrace.LogOutput(PayloadTraceLabels.ClientOutputReassembled, result);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        // Client disconnected / aborted — not an upstream failure.
        if (streamStarted)
        {
            logger.LogInformation("Streaming chat completion cancelled by client.");
            return;
        }

        throw;
    }
    catch (Exception ex) when (streamStarted)
    {
        logger.LogError(ex, "Streaming chat completion failed after response started.");
        var message = ex is HttpRequestException or TimeoutException
            ? "Upstream provider error."
            : "Streaming response failed.";
        await SseStreamErrorWriter.TryWriteAsync(response, message, CancellationToken.None);
    }
}

static bool HasClearDatabaseArg(string[] args) =>
    args.Any(arg =>
        string.Equals(arg, "--clear-db", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arg, "--clear-database", StringComparison.OrdinalIgnoreCase));
