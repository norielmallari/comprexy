using System.Globalization;
using System.Text;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services;

/// <summary>
/// AsyncLocal-backed writer that creates one timestamped file per HTTP request or compression job
/// when <c>Trace:RequestFiles</c> is enabled.
/// </summary>
public sealed class RequestTraceFileSession : IRequestTraceFileSession
{
    private static readonly AsyncLocal<SessionState?> Current = new();

    private readonly TraceOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly IClock _clock;
    private readonly ILogger<RequestTraceFileSession> _logger;

    public RequestTraceFileSession(
        IOptions<TraceOptions> options,
        IHostEnvironment environment,
        IClock clock,
        ILogger<RequestTraceFileSession> logger)
    {
        _options = options.Value;
        _environment = environment;
        _clock = clock;
        _logger = logger;
    }

    public bool IsActive => Current.Value is not null;

    public IDisposable Begin(string? correlationId = null) =>
        BeginInternal(
            kind: "request",
            correlationId: correlationId,
            headerTitle: "Comprexy request trace",
            extraHeaders: null);

    public IDisposable BeginCompression(Guid conversationId, string mode) =>
        BeginInternal(
            kind: "compression",
            correlationId: $"{conversationId:N}"[..12],
            headerTitle: "Comprexy compression trace",
            extraHeaders:
            [
                $"# ConversationId: {conversationId:D}",
                $"# Mode: {mode}"
            ]);

    public void Append(string text)
    {
        var state = Current.Value;
        if (state is null || string.IsNullOrEmpty(text))
        {
            return;
        }

        lock (state.Gate)
        {
            if (state.Disposed)
            {
                return;
            }

            state.Writer.WriteLine($"--- {_clock.UtcNow:O} ---");
            state.Writer.WriteLine(text.TrimEnd());
            state.Writer.WriteLine();
        }
    }

    private IDisposable BeginInternal(
        string kind,
        string? correlationId,
        string headerTitle,
        IReadOnlyList<string>? extraHeaders)
    {
        if (!_options.RequestFiles)
        {
            return NoOpDisposable.Instance;
        }

        if (Current.Value is not null)
        {
            return NoOpDisposable.Instance;
        }

        var directory = ResolveDirectory();
        Directory.CreateDirectory(directory);

        var now = _clock.UtcNow;
        var id = SanitizeFileToken(string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString("N")[..8]
            : correlationId.Trim());
        var fileName = $"{now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}-{kind}-{id}.log";
        var path = Path.Combine(directory, fileName);

        StreamWriter writer;
        try
        {
            writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), Encoding.UTF8)
            {
                AutoFlush = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create {Kind} trace file at {Path}.", kind, path);
            return NoOpDisposable.Instance;
        }

        writer.WriteLine($"# {headerTitle}");
        writer.WriteLine($"# Started: {now:O}");
        writer.WriteLine($"# CorrelationId: {id}");
        if (extraHeaders is not null)
        {
            foreach (var header in extraHeaders)
            {
                writer.WriteLine(header);
            }
        }

        writer.WriteLine($"# File: {path}");
        writer.WriteLine();

        var state = new SessionState(writer, path);
        Current.Value = state;
        _logger.LogInformation("{Kind} trace file opened: {Path}", kind, path);
        return state;
    }

    private string ResolveDirectory()
    {
        var configured = string.IsNullOrWhiteSpace(_options.RequestLogDirectory)
            ? "logs/requests"
            : _options.RequestLogDirectory.Trim();

        return Path.IsPathRooted(configured)
            ? configured
            : Path.GetFullPath(Path.Combine(_environment.ContentRootPath, configured));
    }

    private static string SanitizeFileToken(string value)
    {
        var buffer = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            buffer.Append(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-');
        }

        var sanitized = buffer.ToString().Trim('-');
        return string.IsNullOrEmpty(sanitized) ? Guid.NewGuid().ToString("N")[..8] : sanitized;
    }

    private sealed class SessionState : IDisposable
    {
        public SessionState(StreamWriter writer, string path)
        {
            Writer = writer;
            Path = path;
        }

        public StreamWriter Writer { get; }
        public string Path { get; }
        public object Gate { get; } = new();
        public bool Disposed { get; private set; }

        public void Dispose()
        {
            lock (Gate)
            {
                if (Disposed)
                {
                    return;
                }

                Disposed = true;
                try
                {
                    Writer.WriteLine($"# Ended: {DateTimeOffset.UtcNow:O}");
                    Writer.Dispose();
                }
                finally
                {
                    if (ReferenceEquals(Current.Value, this))
                    {
                        Current.Value = null;
                    }
                }
            }
        }
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public static readonly NoOpDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
