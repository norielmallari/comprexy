using System.Text;
using System.Text.Encodings.Web;
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
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonWriterOptions PrettyWriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly HashSet<string> PreferBlockTextProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "content",
        "reasoning_content",
        "reasoning"
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

    /// <summary>
    /// Human-oriented JSON for audit logs: relaxed escaping, multiline text as <c>|</c> blocks,
    /// and nested JSON in <c>arguments</c> expanded when parseable.
    /// </summary>
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
            var builder = new StringBuilder();
            WriteHumanReadable(document.RootElement, builder, indent: 0, propertyName: null);
            return builder.ToString();
        }
        catch (JsonException)
        {
            return payload;
        }
    }

    private static void WriteHumanReadable(
        JsonElement element,
        StringBuilder builder,
        int indent,
        string? propertyName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('{');
                var firstProperty = true;
                foreach (var property in element.EnumerateObject())
                {
                    if (!firstProperty)
                    {
                        builder.Append(',');
                    }

                    firstProperty = false;
                    builder.AppendLine();
                    AppendIndent(builder, indent + 2);
                    builder.Append(JsonSerializer.Serialize(property.Name, SerializerOptions));
                    builder.Append(": ");
                    WriteHumanReadable(property.Value, builder, indent + 2, property.Name);
                }

                if (!firstProperty)
                {
                    builder.AppendLine();
                    AppendIndent(builder, indent);
                }

                builder.Append('}');
                break;

            case JsonValueKind.Array:
                builder.Append('[');
                var firstItem = true;
                foreach (var item in element.EnumerateArray())
                {
                    if (!firstItem)
                    {
                        builder.Append(',');
                    }

                    firstItem = false;
                    builder.AppendLine();
                    AppendIndent(builder, indent + 2);
                    WriteHumanReadable(item, builder, indent + 2, propertyName: null);
                }

                if (!firstItem)
                {
                    builder.AppendLine();
                    AppendIndent(builder, indent);
                }

                builder.Append(']');
                break;

            case JsonValueKind.String:
                WriteStringValue(element.GetString() ?? string.Empty, builder, indent, propertyName);
                break;

            default:
                using (var stream = new MemoryStream())
                {
                    using (var writer = new Utf8JsonWriter(stream, PrettyWriterOptions))
                    {
                        element.WriteTo(writer);
                    }

                    builder.Append(Encoding.UTF8.GetString(stream.ToArray()).Trim());
                }

                break;
        }
    }

    private static void WriteStringValue(string value, StringBuilder builder, int indent, string? propertyName)
    {
        if (string.Equals(propertyName, "arguments", StringComparison.OrdinalIgnoreCase) &&
            TryWriteNestedJsonString(value, builder, indent))
        {
            return;
        }

        if (ShouldWriteAsBlock(value, propertyName))
        {
            WriteBlockString(value, builder, indent);
            return;
        }

        builder.Append(JsonSerializer.Serialize(value, SerializerOptions));
    }

    private static bool ShouldWriteAsBlock(string value, string? propertyName) =>
        value.Contains('\n') ||
        (propertyName is not null && PreferBlockTextProperties.Contains(propertyName) && value.Length > 80);

    private static bool TryWriteNestedJsonString(string value, StringBuilder builder, int indent)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || (trimmed[0] is not '{' and not '['))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            WriteHumanReadable(document.RootElement, builder, indent, propertyName: null);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void WriteBlockString(string value, StringBuilder builder, int indent)
    {
        builder.Append('|');
        using var reader = new StringReader(value);
        while (reader.ReadLine() is { } line)
        {
            builder.AppendLine();
            AppendIndent(builder, indent + 2);
            builder.Append(line);
        }
    }

    private static void AppendIndent(StringBuilder builder, int indent) =>
        builder.Append(' ', indent);

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
