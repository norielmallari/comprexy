using Comprexy.Domain.Enums;

namespace Comprexy.Application.Configuration;

/// <summary>
/// Token metrics capture and reporting toggles.
/// </summary>
public class MetricsOptions
{
    public const string SectionName = "Metrics";

    /// <summary>
    /// When true, successful compressed-path turns and compression LLM usage are persisted
    /// for conversation-level proof reporting. Default is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Default read-side basis for metrics REST / MCP / rollup projections
    /// (<see cref="PromptTokenBasis.ProviderActual"/>). Persistence and SoftBudget always use
    /// tiktoken estimates. Override per request with <c>?promptTokenBasis=Estimated</c> on
    /// control-api metrics endpoints.
    /// </summary>
    public PromptTokenBasis PromptTokenBasis { get; set; } = PromptTokenBasis.ProviderActual;
}
