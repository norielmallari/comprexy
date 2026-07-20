namespace Comprexy.Application.Configuration;

/// <summary>
/// Configuration for the model used to perform LLM-based context compression. When any field is
/// left unset, the corresponding value from <see cref="ProviderOptions"/> is used, so the same
/// upstream model performs compression by default.
/// </summary>
public class CompressionOptions
{
    public const string SectionName = "Compression";

    public string? BaseUrl { get; set; }

    public string? ApiKey { get; set; }

    public string? Model { get; set; }

    /// <summary>
    /// Timeout for compression calls. When null, falls back to <see cref="ProviderOptions.TimeoutSeconds"/>.
    /// compression prompts are often larger/slower than chat turns, so prefer a generous value for local models.
    /// </summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>Sampling temperature used for compression calls. Kept low for deterministic summaries.</summary>
    public double Temperature { get; set; } = 0.2;

    /// <summary>
    /// When false (default), compression requests send
    /// <c>chat_template_kwargs.enable_thinking=false</c> so reasoning models do not emit
    /// thinking into the working-memory reply.
    /// </summary>
    public bool EnableThinking { get; set; }

    /// <summary>
    /// Path to the markdown file containing the Fixed compression system instruction.
    /// Relative paths are resolved from the API content root.
    /// </summary>
    public string InstructionFile { get; set; } = "Prompts/compression-fixed.md";

    /// <summary>
    /// Path to the Smart compression trailing user-instruction (markdown WM + Retain Sequences).
    /// Prefixed onto the live chat message list with a server-built retain index.
    /// Used when <see cref="ContextPolicyOptions.RetainSelection"/> is Smart.
    /// </summary>
    public string SmartInstructionFile { get; set; } = "Prompts/compression-smart.md";
}
