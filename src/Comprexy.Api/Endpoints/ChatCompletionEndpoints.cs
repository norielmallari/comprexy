using System.Text;
using System.Text.Json;
using Comprexy.Api.Contracts;
using Comprexy.Api.Mapping;
using Comprexy.Api.Streaming;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Services;
using Comprexy.Application.Tracing;

namespace Comprexy.Api.Endpoints;

public static class ChatCompletionEndpoints
{
    public static IEndpointRouteBuilder MapChatCompletionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/chat/completions", HandleAsync);
        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        ProxyChatCompletionService proxyService,
        IPayloadTraceLogger payloadTrace,
        IRequestTraceFileSession requestTraceFiles,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Comprexy.Api.ChatCompletions");
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

            var conversationIdHeader = httpContext.Request.Headers[ComprexyHeaders.ConversationId].FirstOrDefault();
            var incomingRequest = ChatCompletionRequestParser.Parse(rawRequest, conversationIdHeader);

            if (incomingRequest.Stream)
            {
                await StreamChatCompletionAsync(httpContext, proxyService, payloadTrace, logger, incomingRequest, cancellationToken);
                return Results.Empty;
            }

            var result = await proxyService.HandleAsync(incomingRequest, cancellationToken);
            httpContext.Response.Headers[ComprexyHeaders.ConversationId] = result.ConversationId.ToString();

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
    }

    private static async Task StreamChatCompletionAsync(
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
                    response.Headers[ComprexyHeaders.ConversationId] = conversationId.ToString();
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
}
