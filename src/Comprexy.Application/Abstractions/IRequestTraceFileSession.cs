namespace Comprexy.Application.Abstractions;

/// <summary>
/// Opens a per-request or per-compression timestamped log file and routes payload traces into it
/// for the duration of the ambient async context.
/// </summary>
public interface IRequestTraceFileSession
{
    /// <summary>
    /// Starts a new request file when <c>Trace:RequestFiles</c> is enabled. Dispose to flush
    /// and close the file. Nested begins are no-ops that return a no-op disposable.
    /// </summary>
    IDisposable Begin(string? correlationId = null);

    /// <summary>
    /// Starts a compression-only file for background (or sync) compression work. Needed because
    /// background jobs run after the HTTP request file has already closed.
    /// </summary>
    IDisposable BeginCompression(Guid conversationId, string mode);

    /// <summary>Appends a formatted payload section to the active request file, if any.</summary>
    void Append(string text);

    bool IsActive { get; }
}
