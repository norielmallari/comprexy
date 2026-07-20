namespace Comprexy.Application.Configuration;

/// <summary>
/// Whether prepare may run synchronous emergency compression when the hard token budget is hit.
/// </summary>
public enum EmergencyCompressionMode
{
    /// <summary>
    /// Never block the chat path on emergency compression. Over hard: send-time retain trim, then
    /// HTTP 413 if still over. Soft background compression remains the recovery path.
    /// </summary>
    Off = 0,

    /// <summary>
    /// Run bounded synchronous emergency compression before forwarding when at or above hard.
    /// </summary>
    Sync = 1
}
