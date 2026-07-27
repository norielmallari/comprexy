namespace Comprexy.Application.Models.Retrieval;

/// <summary>
/// Bounds for conversation retrieval (RAG) projections.
/// Row take reuses <see cref="Comprexy.Application.Models.Telemetry.TelemetryQueryLimits"/>; snippet/wire caps are retrieval-specific.
/// </summary>
public static class RetrievalQueryLimits
{
    public const int DefaultMaxSnippetChars = 500;

    public const int DefaultMaxWireJsonChars = 4_096;

    public static string Truncate(string? text, int maxChars = DefaultMaxSnippetChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
        {
            return text ?? string.Empty;
        }

        return text[..maxChars].TrimEnd() + "…";
    }
}
