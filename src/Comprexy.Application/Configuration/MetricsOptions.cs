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
}
