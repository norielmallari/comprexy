using System.Text;
using System.Text.Json;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Tracing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services;

public class PayloadTraceLogger : IPayloadTraceLogger
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private static readonly JsonWriterOptions PrettyWriterOptions = new()
    {
        Indented = true
    };

    private readonly TraceOptions _options;
    private readonly IRequestTraceFileSession _requestFiles;
    private readonly ILogger<PayloadTraceLogger> _logger;

    public PayloadTraceLogger(
        IOptions<TraceOptions> options,
        IRequestTraceFileSession requestFiles,
        ILogger<PayloadTraceLogger> logger)
    {
        _options = options.Value;
        _requestFiles = requestFiles;
        _logger = logger;
    }

    public void LogInput(string label, string payload) => Write(label, payload, requireCategoryForRequestFile: false);

    public void LogOutput(string label, string payload) => Write(label, payload, requireCategoryForRequestFile: false);

    public void LogInput(string label, object payload) => Write(label, Serialize(payload), requireCategoryForRequestFile: false);

    public void LogOutput(string label, object payload) => Write(label, Serialize(payload), requireCategoryForRequestFile: false);

    public void LogStreamingChunk(string label, string payload) =>
        Write(label, payload, requireCategoryForRequestFile: true);

    private void Write(string label, string payload, bool requireCategoryForRequestFile)
    {
        var category = PayloadTraceLabels.ResolveCategory(label);
        var categoryEnabled = _options.IsEnabled(category);
        // Request files are normally the full audit trail — independent of console category toggles.
        // Streaming chunks opt into request files only when the category flag is on (avoid floods).
        var writeToRequestFile = _options.RequestFiles
            && (!requireCategoryForRequestFile || categoryEnabled);
        var writeToConsole = categoryEnabled && _logger.IsEnabled(LogLevel.Trace);

        if (!writeToConsole && !writeToRequestFile)
        {
            return;
        }

        var message = FormatMessage(label, payload);
        if (writeToConsole)
        {
            _logger.LogTrace("{Message}", message);
        }

        if (writeToRequestFile)
        {
            _requestFiles.Append(message);
        }
    }

    private static string Serialize(object payload) =>
        JsonSerializer.Serialize(payload, SerializerOptions);

    private string FormatMessage(string label, string payload)
    {
        var formattedPayload = Truncate(PrettyPrint(payload));

        var builder = new StringBuilder();
        builder.AppendLine(label);
        builder.AppendLine("  payload:");
        AppendIndentedPayload(builder, formattedPayload);
        return builder.ToString();
    }

    private static string PrettyPrint(string payload)
    {
        var trimmed = payload.Trim();
        if (trimmed.Length == 0)
        {
            return payload;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, PrettyWriterOptions))
            {
                document.WriteTo(writer);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return payload;
        }
    }

    private static void AppendIndentedPayload(StringBuilder builder, string payload)
    {
        using var reader = new StringReader(payload);
        while (reader.ReadLine() is { } line)
        {
            builder.Append("    ");
            builder.AppendLine(line);
        }
    }

    private string Truncate(string payload)
    {
        if (_options.MaxPayloadChars <= 0 || payload.Length <= _options.MaxPayloadChars)
        {
            return payload;
        }

        return $"{payload[.._options.MaxPayloadChars]}…[truncated {payload.Length - _options.MaxPayloadChars} chars]";
    }
}
