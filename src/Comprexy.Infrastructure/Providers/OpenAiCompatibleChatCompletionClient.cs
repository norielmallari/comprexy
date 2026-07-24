using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services;
using Comprexy.Application.Tracing;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comprexy.Infrastructure.Providers;

/// <summary>
/// Calls any OpenAI-compatible <c>/chat/completions</c> endpoint, preserving unknown request
/// fields from the original client payload whenever one is provided.
/// </summary>
public class OpenAiCompatibleChatCompletionClient : IChatCompletionClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null
    };

    private readonly HttpClient _httpClient;
    private readonly IPayloadTraceLogger _payloadTrace;
    private readonly ProxyOptions _proxyOptions;
    private readonly CompressionOptions _compressionOptions;
    private readonly ILogger<OpenAiCompatibleChatCompletionClient> _logger;

    public OpenAiCompatibleChatCompletionClient(
        HttpClient httpClient,
        IPayloadTraceLogger payloadTrace,
        IOptions<ProxyOptions> proxyOptions,
        IOptions<CompressionOptions> compressionOptions,
        ILogger<OpenAiCompatibleChatCompletionClient> logger)
    {
        _httpClient = httpClient;
        _payloadTrace = payloadTrace;
        _proxyOptions = proxyOptions.Value;
        _compressionOptions = compressionOptions.Value;
        _logger = logger;
    }

    public async Task<UpstreamChatResult> CompleteAsync(
        ProviderEndpoint endpoint,
        UpstreamRequest request,
        CancellationToken cancellationToken)
    {
        var baseUrl = endpoint.BaseUrl.TrimEnd('/');
        var labels = GetTraceLabels(request.Purpose);
        var wireBody = BuildWireBody(endpoint, request with { Stream = false });
        _payloadTrace.LogInput(labels.Input, wireBody);
        using var httpRequest = CreateHttpRequest(endpoint, wireBody);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, endpoint.TimeoutSeconds)));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("Upstream provider at {BaseUrl} timed out after {TimeoutSeconds}s.", baseUrl, endpoint.TimeoutSeconds);
            throw new TimeoutException($"Upstream provider at {baseUrl} timed out after {endpoint.TimeoutSeconds}s.");
        }

        using var responseScope = response;
        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Upstream provider at {BaseUrl} returned {StatusCode}: {Body}", baseUrl, (int)response.StatusCode, rawBody);
            throw new HttpRequestException($"Upstream provider returned {(int)response.StatusCode}: {rawBody}");
        }

        _payloadTrace.LogOutput(labels.Output, rawBody);

        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawBody) ? "{}" : rawBody);
        var root = document.RootElement;
        var choice = root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array
            ? choices.EnumerateArray().FirstOrDefault()
            : default;

        string content = string.Empty;
        string? finishReason = null;
        string? assistantMessageJson = null;
        if (choice.ValueKind == JsonValueKind.Object)
        {
            if (choice.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
            {
                assistantMessageJson = message.GetRawText();
                if (message.TryGetProperty("content", out var messageContent))
                {
                    content = messageContent.ValueKind == JsonValueKind.String
                        ? messageContent.GetString() ?? string.Empty
                        : messageContent.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                            ? string.Empty
                            : messageContent.GetRawText();
                }
            }

            if (choice.TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String)
            {
                finishReason = finish.GetString();
            }
        }

        int? promptTokens = null;
        int? completionTokens = null;
        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            if (usage.TryGetProperty("prompt_tokens", out var prompt) && prompt.ValueKind == JsonValueKind.Number)
            {
                promptTokens = prompt.GetInt32();
            }

            if (usage.TryGetProperty("completion_tokens", out var completion) && completion.ValueKind == JsonValueKind.Number)
            {
                completionTokens = completion.GetInt32();
            }
        }

        return new UpstreamChatResult(
            content,
            finishReason,
            promptTokens,
            completionTokens,
            rawBody,
            assistantMessageJson);
    }

    public async Task<UpstreamChatResult> StreamAsync(
        ProviderEndpoint endpoint,
        UpstreamRequest request,
        Func<string, CancellationToken, Task> onRawSseData,
        CancellationToken cancellationToken)
    {
        var baseUrl = endpoint.BaseUrl.TrimEnd('/');
        var labels = GetTraceLabels(request.Purpose);
        var wireBody = BuildWireBody(endpoint, request with { Stream = true });
        _payloadTrace.LogInput(labels.Input, wireBody);
        using var httpRequest = CreateHttpRequest(endpoint, wireBody);
        using var response = await SendStreamingRequestAsync(httpRequest, endpoint, baseUrl, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Upstream provider at {BaseUrl} returned {StatusCode}: {Body}",
                baseUrl,
                (int)response.StatusCode,
                body);
            throw new HttpRequestException($"Upstream provider returned {(int)response.StatusCode}: {body}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var assembler = new StreamingAssistantMessageAssembler();
        string? finishReason = null;
        int? promptTokens = null;
        int? completionTokens = null;

        try
        {
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var data = line["data:".Length..].TrimStart();
                if (data.Length == 0)
                {
                    continue;
                }

                _payloadTrace.LogStreamingChunk(labels.Output, data);
                await onRawSseData(data, cancellationToken);

                if (data == "[DONE]")
                {
                    break;
                }

                using var payloadDoc = JsonDocument.Parse(data);
                var payload = payloadDoc.RootElement;
                var choice = payload.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array
                    ? choices.EnumerateArray().FirstOrDefault()
                    : default;

                if (payload.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                {
                    if (usage.TryGetProperty("prompt_tokens", out var prompt) && prompt.ValueKind == JsonValueKind.Number)
                    {
                        promptTokens = prompt.GetInt32();
                    }

                    if (usage.TryGetProperty("completion_tokens", out var completion) && completion.ValueKind == JsonValueKind.Number)
                    {
                        completionTokens = completion.GetInt32();
                    }
                }

                if (choice.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (choice.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object)
                {
                    assembler.MergeDelta(delta);
                }

                if (choice.TryGetProperty("finish_reason", out var finish) &&
                    finish.ValueKind == JsonValueKind.String)
                {
                    finishReason = finish.GetString();
                }
            }
        }
        catch (Exception ex) when (
            !cancellationToken.IsCancellationRequested &&
            UpstreamStreamEndDetector.IsPrematureUpstreamStreamEnd(ex))
        {
            if (string.IsNullOrEmpty(assembler.Content) && finishReason is null)
            {
                throw new HttpRequestException("Upstream stream ended prematurely.", ex);
            }

            _logger.LogWarning(
                ex,
                "Upstream provider at {BaseUrl} ended the stream before [DONE]; returning partial assembly.",
                baseUrl);
        }

        var assistantMessageJson = assembler.BuildMessageJson(SerializerOptions);
        var result = new UpstreamChatResult(
            assembler.Content,
            finishReason,
            promptTokens,
            completionTokens,
            AssistantMessageJson: assistantMessageJson);
        _payloadTrace.LogOutput(labels.OutputReassembled, result);
        return result;
    }

    private static TraceLabelSet GetTraceLabels(UpstreamRequestPurpose purpose) =>
        purpose switch
        {
            UpstreamRequestPurpose.Compression => new TraceLabelSet(
                PayloadTraceLabels.CompressionModelInput,
                PayloadTraceLabels.CompressionModelOutput,
                PayloadTraceLabels.CompressionModelOutputReassembled),
            _ => new TraceLabelSet(
                PayloadTraceLabels.ModelInput,
                PayloadTraceLabels.ModelOutput,
                PayloadTraceLabels.ModelOutputReassembled)
        };

    private async Task<HttpResponseMessage> SendStreamingRequestAsync(
        HttpRequestMessage request,
        ProviderEndpoint endpoint,
        string baseUrl,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, endpoint.TimeoutSeconds)));

        try
        {
            return await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                "Upstream provider at {BaseUrl} timed out after {TimeoutSeconds}s.",
                baseUrl,
                endpoint.TimeoutSeconds);
            throw new TimeoutException($"Upstream provider at {baseUrl} timed out after {endpoint.TimeoutSeconds}s.");
        }
    }

    private string BuildWireBody(ProviderEndpoint endpoint, UpstreamRequest request)
    {
        JsonObject root;
        var effectiveRequest = request.RewrittenClientRequest ?? request.OriginalClientRequest;
        if (effectiveRequest is { ValueKind: JsonValueKind.Object } original)
        {
            root = JsonNode.Parse(original.GetRawText()) as JsonObject
                ?? throw new InvalidOperationException("Unable to parse original client request.");
        }
        else
        {
            root = new JsonObject();
            var callOptions = request.CallOptions ?? new ChatCompletionCallOptions();
            if (callOptions.Temperature is not null)
            {
                root["temperature"] = callOptions.Temperature;
            }

            if (callOptions.TopP is not null)
            {
                root["top_p"] = callOptions.TopP;
            }

            if (callOptions.MaxTokens is not null)
            {
                root["max_tokens"] = callOptions.MaxTokens;
            }

            if (callOptions.Stop is not null)
            {
                root["stop"] = new JsonArray(callOptions.Stop.Select(s => (JsonNode?)s).ToArray());
            }
        }

        root["stream"] = request.Stream;

        if (endpoint.HasConfiguredModel)
        {
            root["model"] = endpoint.Model;
        }

        if (request.Purpose == UpstreamRequestPurpose.Compression)
        {
            root["chat_template_kwargs"] = new JsonObject
            {
                ["enable_thinking"] = _compressionOptions.EnableThinking
            };
        }

        if (request.ReplaceMessages || root["messages"] is null)
        {
            var messages = new JsonArray();
            foreach (var message in request.Messages)
            {
                messages.Add(BuildMessageNode(message));
            }

            root["messages"] = messages;
        }
        else
        {
            ReasoningContentStripper.StripFromMessagesArray(
                root["messages"],
                _proxyOptions.StripReasoningContent);
        }

        return root.ToJsonString(SerializerOptions);
    }

    private JsonNode BuildMessageNode(ChatMessage message)
    {
        if (message.RawWireMessage is { ValueKind: JsonValueKind.Object } raw)
        {
            var node = JsonNode.Parse(raw.GetRawText())
                ?? throw new InvalidOperationException("Unable to parse raw wire message.");
            if (_proxyOptions.StripReasoningContent && node is JsonObject obj)
            {
                ReasoningContentStripper.StripFromMessageObject(obj);
            }

            return node;
        }

        return new JsonObject
        {
            ["role"] = ToWireRole(message.Role),
            ["content"] = message.Content
        };
    }

    private static HttpRequestMessage CreateHttpRequest(ProviderEndpoint endpoint, string jsonBody)
    {
        var baseUrl = endpoint.BaseUrl.TrimEnd('/');
        var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(endpoint.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.ApiKey);
        }

        return request;
    }

    private static string ToWireRole(MessageRole role) => role switch
    {
        MessageRole.System => "system",
        MessageRole.User => "user",
        MessageRole.Assistant => "assistant",
        MessageRole.Tool => "tool",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unsupported message role.")
    };

    private sealed record TraceLabelSet(string Input, string Output, string OutputReassembled);
}
