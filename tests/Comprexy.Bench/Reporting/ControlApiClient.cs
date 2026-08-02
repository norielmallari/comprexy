using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Comprexy.Bench.Reporting;

/// <summary>Subset of the control-api metrics contract the bench report needs.</summary>
internal sealed record ConversationSummaryResponse(
    Guid ConversationId,
    int TotalTurns,
    long TotalRawInputTokensEstimated,
    long TotalCompressedPromptTokens,
    long TotalCompletionTokens,
    long TotalCompressionOverheadTokens,
    long TotalBaselineTokensEstimated,
    long TotalActualTokensEstimated,
    long TotalNetTokensSaved,
    double AverageTokenSavingsRatio,
    int CompressionEventCount);

internal sealed record ConversationTurnResponse(
    int TurnIndex,
    string Model,
    int RawInputTokensEstimated,
    int CompressedInputTokensEstimated,
    int? ActualPromptTokens,
    int ActualCompletionTokens,
    int BaselineTotalTokensEstimated,
    int CompressedTotalTokensEstimated,
    int NetTokensSaved,
    double NetTokenSavingsRatio,
    bool SoftBudgetExceeded,
    bool HardBudgetExceeded,
    bool TrimTriggered,
    int RawMessageCount,
    int SentMessageCount,
    int? DurationMs,
    int? UpstreamDurationMs,
    int? PrepareDurationMs);

/// <summary>
/// Reads the bench run's stored turn metrics. Token numbers in a bench report come from here —
/// the harness never recomputes savings from its own view of the conversation.
/// </summary>
internal sealed class ControlApiClient : IDisposable
{
    private readonly HttpClient _client;

    public ControlApiClient(string baseUrl, string? apiKey = null)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        _client = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    public string BaseUrl { get; }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.GetAsync("/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public async Task<ConversationSummaryResponse?> GetSummaryAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var response = await _client.GetAsync(
            $"/v1/comprexy/conversations/{conversationId}/metrics?promptTokenBasis=ProviderActual", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ConversationSummaryResponse>(
            BenchJson.Options, cancellationToken);
    }

    public async Task<IReadOnlyList<ConversationTurnResponse>> GetTurnsAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var response = await _client.GetAsync(
            $"/v1/comprexy/conversations/{conversationId}/metrics/turns?promptTokenBasis=ProviderActual", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ConversationTurnResponse>>(
            BenchJson.Options, cancellationToken) ?? [];
    }

    public void Dispose() => _client.Dispose();
}
