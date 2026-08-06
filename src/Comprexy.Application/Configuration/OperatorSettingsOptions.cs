namespace Comprexy.Application.Configuration;

/// <summary>
/// Host options for the SQLite operator-settings overlay poller.
/// </summary>
public class OperatorSettingsOptions
{
    public const string SectionName = "OperatorSettings";

    /// <summary>How often proxy/control-api poll <c>OperatorSettings.Revision</c>. Default 2s.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
}