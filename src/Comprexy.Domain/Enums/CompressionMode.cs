namespace Comprexy.Domain.Enums;

/// <summary>
/// Describes why a compression job was triggered and how urgently it must run.
/// </summary>
public enum CompressionMode
{
    /// <summary>Runs after the response was already returned to the client.</summary>
    Background,

    /// <summary>Same as background but should be processed ahead of normal jobs.</summary>
    HighPriorityBackground,

    /// <summary>Runs synchronously before forwarding the request because the hard limit was exceeded.</summary>
    Emergency
}
