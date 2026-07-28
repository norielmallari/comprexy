namespace Comprexy.Application.Configuration;

/// <summary>
/// Configuration for the Compression endpoint (ToolSchema mapper) and Inline wrap-up prompts.
/// When BaseUrl/ApiKey/Model/Timeout are unset, the corresponding <see cref="ProviderOptions"/>
/// values are used. Inline wrap-up itself uses the live chat endpoint; these knobs still drive
/// ToolSchema mapping. When both <see cref="Model"/> and <see cref="ProviderOptions.Model"/> are
/// null, callers fall back to the client chat model.
/// </summary>
public class CompressionOptions
{
    public const string SectionName = "Compression";

    public string? BaseUrl { get; set; }

    public string? ApiKey { get; set; }

    public string? Model { get; set; }

    /// <summary>
    /// Timeout for Compression-endpoint calls (e.g. ToolSchema mapper). When null, falls back to
    /// <see cref="ProviderOptions.TimeoutSeconds"/>.
    /// </summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>Sampling temperature used for Compression-endpoint calls.</summary>
    public double Temperature { get; set; } = 0.6;

    /// <summary>
    /// When false (default), Compression-endpoint requests send
    /// <c>chat_template_kwargs.enable_thinking=false</c> so reasoning models do not emit
    /// thinking into mapper replies.
    /// </summary>
    public bool EnableThinking { get; set; }

    /// <summary>
    /// Path to the Inline follow-up wrap-up user prompt (return-only working memory).
    /// Relative to API content root.
    /// </summary>
    public string InlineInstructionFile { get; set; } = "Prompts/compression-inline.md";

    /// <summary>
    /// Shared working-memory markdown skeleton appended to Inline wrap-up prompts.
    /// </summary>
    public string WorkingMemoryTemplateFile { get; set; } = "Prompts/working-memory-template.md";
}
