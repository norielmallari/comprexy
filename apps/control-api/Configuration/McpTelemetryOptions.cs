namespace Comprexy.ControlApi.Configuration;

public sealed class McpTelemetryOptions
{
    public const string SectionName = "McpTelemetry";

    public int DefaultRowLimit { get; set; } = 100;

    public int MaxRowLimit { get; set; } = 1000;

    public int QueryTimeoutSeconds { get; set; } = 5;
}
