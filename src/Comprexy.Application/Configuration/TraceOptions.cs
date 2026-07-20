using Comprexy.Application.Tracing;

namespace Comprexy.Application.Configuration;

/// <summary>
/// Controls payload tracing. Category flags gate <b>console</b> Trace output (requires
/// <c>Logging:LogLevel:Comprexy</c> = <c>Trace</c>). <see cref="RequestFiles"/> writes a full
/// per-request / per-compression audit file independently of those console toggles.
/// </summary>
public class TraceOptions
{
    public const string SectionName = "Trace";

    /// <summary>Console: raw request body from the client (<c>client input</c>).</summary>
    public bool ClientInput { get; set; }

    /// <summary>Console: responses to the client; streaming logs only the reassembled completion (<c>client output</c>).</summary>
    public bool ClientOutput { get; set; }

    /// <summary>Console: request body to the upstream chat model (<c>model input</c>).</summary>
    public bool ModelInput { get; set; }

    /// <summary>
    /// Console: responses from the upstream chat model (<c>model output</c>). For streaming,
    /// each SSE data chunk is logged as it arrives, then the reassembled completion.
    /// </summary>
    public bool ModelOutput { get; set; }

    /// <summary>Console: request body to the compression model (<c>compression model input</c>).</summary>
    public bool CompressionModelInput { get; set; }

    /// <summary>
    /// Console: responses from the compression model (<c>compression model output</c>).
    /// Streaming compression chunks follow the same per-chunk behavior as <see cref="ModelOutput"/>.
    /// </summary>
    public bool CompressionModelOutput { get; set; }

    /// <summary>Console: context-budget token estimates and decisions (<c>context budget</c>).</summary>
    public bool ContextBudget { get; set; }

    /// <summary>
    /// When true, every payload category is written to timestamped files under
    /// <see cref="RequestLogDirectory"/> (one file per HTTP request, plus one per background
    /// compression job), regardless of the console category toggles above.
    /// </summary>
    public bool RequestFiles { get; set; }

    /// <summary>
    /// Directory for per-request / compression trace files. Relative paths are resolved from the API content root.
    /// </summary>
    public string RequestLogDirectory { get; set; } = "logs/requests";

    /// <summary>
    /// Maximum characters per logged payload (console and files). Longer payloads are truncated.
    /// Set to 0 for no truncation.
    /// </summary>
    public int MaxPayloadChars { get; set; } = 32_768;

    /// <summary>Returns whether the given payload category is enabled for Trace logging.</summary>
    public bool IsEnabled(PayloadTraceCategory category) => category switch
    {
        PayloadTraceCategory.ClientInput => ClientInput,
        PayloadTraceCategory.ClientOutput => ClientOutput,
        PayloadTraceCategory.ModelInput => ModelInput,
        PayloadTraceCategory.ModelOutput => ModelOutput,
        PayloadTraceCategory.CompressionModelInput => CompressionModelInput,
        PayloadTraceCategory.CompressionModelOutput => CompressionModelOutput,
        PayloadTraceCategory.ContextBudget => ContextBudget,
        _ => false
    };
}
