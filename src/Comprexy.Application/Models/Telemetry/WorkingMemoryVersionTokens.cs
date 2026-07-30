namespace Comprexy.Application.Models.Telemetry;

/// <summary>
/// Version and stored token count of a working-memory snapshot, without its content.
/// </summary>
public sealed class WorkingMemoryVersionTokens
{
    public int Version { get; init; }

    public int TokenCount { get; init; }
}
