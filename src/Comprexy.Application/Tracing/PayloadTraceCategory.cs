namespace Comprexy.Application.Tracing;

/// <summary>
/// Payload categories that can be independently enabled for Trace logging.
/// </summary>
public enum PayloadTraceCategory
{
    ClientInput,
    ClientOutput,
    ModelInput,
    ModelOutput,
    CompressionModelInput,
    CompressionModelOutput,
    ContextBudget
}
