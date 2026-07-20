namespace Comprexy.Application.Abstractions;

/// <summary>
/// Emits Trace-level payload logs when <see cref="Configuration.TraceOptions"/> enables them.
/// Labels should use <see cref="Tracing.PayloadTraceLabels"/>.
/// </summary>
public interface IPayloadTraceLogger
{
    void LogInput(string label, string payload);

    void LogOutput(string label, string payload);

    void LogInput(string label, object payload);

    void LogOutput(string label, object payload);

    /// <summary>
    /// Logs one streaming wire chunk. Unlike <see cref="LogOutput"/>, request-file writes also
    /// require the matching category flag so chunk floods stay opt-in (e.g. <c>ModelOutput</c>).
    /// </summary>
    void LogStreamingChunk(string label, string payload);
}
