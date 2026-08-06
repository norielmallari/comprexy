using System.Text.Json;
using System.Text.Json.Serialization;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Services.Settings;

/// <summary>
/// Serialize/validate allowlisted operator mutable settings (rejects unknown top-level sections
/// and secret-shaped keys).
/// </summary>
public static class OperatorMutableSettingsJson
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = false
    };

    private static readonly HashSet<string> AllowedTopLevel = new(StringComparer.OrdinalIgnoreCase)
    {
        "proxy",
        "contextPolicy",
        "cacheAlignment",
        "metrics",
        "toolSchema"
    };

    private static readonly HashSet<string> ForbiddenKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "apiKey",
        "requiredApiKey",
        "dashboardApiKey",
        "connectionString",
        "connectionStrings",
        "auth",
        "provider",
        "compression",
        "cors",
        "trace",
        "benchOrchestration",
        "mcpTelemetry",
        "tokenEstimateCache"
    };

    public static OperatorMutableSettingsDto Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
        {
            return new OperatorMutableSettingsDto();
        }

        using var doc = JsonDocument.Parse(json);
        RejectUnknownOrForbidden(doc.RootElement);
        return JsonSerializer.Deserialize<OperatorMutableSettingsDto>(json, JsonOptions)
            ?? new OperatorMutableSettingsDto();
    }

    public static string Serialize(OperatorMutableSettingsDto dto) =>
        JsonSerializer.Serialize(dto, JsonOptions);

    public static void RejectUnknownOrForbidden(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Settings JSON must be an object.");
        }

        foreach (var prop in root.EnumerateObject())
        {
            if (ForbiddenKeys.Contains(prop.Name))
            {
                throw new ArgumentException($"Settings key '{prop.Name}' is not mutable via operator store.");
            }

            if (!AllowedTopLevel.Contains(prop.Name))
            {
                throw new ArgumentException($"Unknown settings section '{prop.Name}'.");
            }

            RejectForbiddenNested(prop.Value);
        }
    }

    private static void RejectForbiddenNested(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var prop in element.EnumerateObject())
        {
            if (ForbiddenKeys.Contains(prop.Name))
            {
                throw new ArgumentException($"Settings key '{prop.Name}' is not mutable via operator store.");
            }

            if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                RejectForbiddenNested(prop.Value);
            }
        }
    }

    /// <summary>
    /// Applies allowlisted overlay fields onto options. Skips keys present in higher-priority
    /// configuration (env / command-line) when <paramref name="isHigherPriority"/> returns true.
    /// </summary>
    public static void ApplyOverlayToProxy(
        ProxyOptions options,
        OperatorMutableSettingsDto dto,
        Func<string, bool>? isHigherPriority = null)
    {
        if (dto.Proxy is null)
        {
            return;
        }

        var p = dto.Proxy;
        if (p.PassThrough is bool passThrough && !IsHigher("Proxy:PassThrough"))
        {
            options.PassThrough = passThrough;
        }

        if (p.OptimizationMode is OptimizationMode mode && !IsHigher("Proxy:OptimizationMode"))
        {
            options.OptimizationMode = mode;
        }

        if (p.StripReasoningContent is bool strip && !IsHigher("Proxy:StripReasoningContent"))
        {
            options.StripReasoningContent = strip;
        }

        bool IsHigher(string key) => isHigherPriority?.Invoke(key) == true;
    }

    public static void ApplyOverlayToContextPolicy(
        ContextPolicyOptions options,
        OperatorMutableSettingsDto dto,
        Func<string, bool>? isHigherPriority = null)
    {
        if (dto.ContextPolicy is null)
        {
            return;
        }

        var c = dto.ContextPolicy;
        if (c.SoftLimitTokens is int soft && !IsHigher("ContextPolicy:SoftLimitTokens"))
        {
            options.SoftLimitTokens = soft;
        }

        if (c.MinTurnsBetweenGenerations is int minTurns && !IsHigher("ContextPolicy:MinTurnsBetweenGenerations"))
        {
            options.MinTurnsBetweenGenerations = minTurns;
        }

        if (c.CompressionRetainMessageCount is int retain && !IsHigher("ContextPolicy:CompressionRetainMessageCount"))
        {
            options.CompressionRetainMessageCount = retain;
        }

        if (c.DedupeDuplicateFailedEdits is bool dedupe && !IsHigher("ContextPolicy:DedupeDuplicateFailedEdits"))
        {
            options.DedupeDuplicateFailedEdits = dedupe;
        }

        if (c.TokenizerEncoding is string encoding
            && !string.IsNullOrWhiteSpace(encoding)
            && !IsHigher("ContextPolicy:TokenizerEncoding"))
        {
            options.TokenizerEncoding = encoding;
        }

        bool IsHigher(string key) => isHigherPriority?.Invoke(key) == true;
    }

    public static void ApplyOverlayToCacheAlignment(
        CacheAlignmentOptions options,
        OperatorMutableSettingsDto dto,
        Func<string, bool>? isHigherPriority = null)
    {
        if (dto.CacheAlignment is null)
        {
            return;
        }

        var c = dto.CacheAlignment;
        if (c.Enabled is bool enabled && !IsHigher("CacheAlignment:Enabled"))
        {
            options.Enabled = enabled;
        }

        if (c.MaxConversations is int max && !IsHigher("CacheAlignment:MaxConversations"))
        {
            options.MaxConversations = max;
        }

        bool IsHigher(string key) => isHigherPriority?.Invoke(key) == true;
    }

    public static void ApplyOverlayToMetrics(
        MetricsOptions options,
        OperatorMutableSettingsDto dto,
        Func<string, bool>? isHigherPriority = null)
    {
        if (dto.Metrics is null)
        {
            return;
        }

        var m = dto.Metrics;
        if (m.Enabled is bool enabled && !IsHigher("Metrics:Enabled"))
        {
            options.Enabled = enabled;
        }

        if (m.PromptTokenBasis is PromptTokenBasis basis && !IsHigher("Metrics:PromptTokenBasis"))
        {
            options.PromptTokenBasis = basis;
        }

        bool IsHigher(string key) => isHigherPriority?.Invoke(key) == true;
    }

    public static void ApplyOverlayToToolSchema(
        ToolSchemaOptions options,
        OperatorMutableSettingsDto dto,
        Func<string, bool>? isHigherPriority = null)
    {
        if (dto.ToolSchema is null)
        {
            return;
        }

        var t = dto.ToolSchema;
        if (t.Mode is ToolSchemaMode mode && !IsHigher("ToolSchema:Mode"))
        {
            options.Mode = mode;
        }

        if (t.ExcludeFromModelTools is { } exclude && !IsHigher("ToolSchema:ExcludeFromModelTools"))
        {
            options.ExcludeFromModelTools = [.. exclude];
        }

        if (t.MappingMaxRetries is int mappingRetries && !IsHigher("ToolSchema:MappingMaxRetries"))
        {
            options.MappingMaxRetries = mappingRetries;
        }

        if (t.MaxRangeLines is int maxRange && !IsHigher("ToolSchema:MaxRangeLines"))
        {
            options.MaxRangeLines = maxRange;
        }

        if (t.MaxSearchMatches is int maxSearch && !IsHigher("ToolSchema:MaxSearchMatches"))
        {
            options.MaxSearchMatches = maxSearch;
        }

        if (t.MaxDirListEntries is int maxDir && !IsHigher("ToolSchema:MaxDirListEntries"))
        {
            options.MaxDirListEntries = maxDir;
        }

        if (t.MaxShellObservationChars is int maxShell && !IsHigher("ToolSchema:MaxShellObservationChars"))
        {
            options.MaxShellObservationChars = maxShell;
        }

        if (t.MaxPassthroughObservationChars is int maxPass
            && !IsHigher("ToolSchema:MaxPassthroughObservationChars"))
        {
            options.MaxPassthroughObservationChars = maxPass;
        }

        if (t.MaxSearchPreviewChars is int maxPreview && !IsHigher("ToolSchema:MaxSearchPreviewChars"))
        {
            options.MaxSearchPreviewChars = maxPreview;
        }

        if (t.MaxManifestImports is int maxImports && !IsHigher("ToolSchema:MaxManifestImports"))
        {
            options.MaxManifestImports = maxImports;
        }

        if (t.MaxManifestSymbols is int maxSymbols && !IsHigher("ToolSchema:MaxManifestSymbols"))
        {
            options.MaxManifestSymbols = maxSymbols;
        }

        if (t.MaxManifestImportChars is int maxImportChars && !IsHigher("ToolSchema:MaxManifestImportChars"))
        {
            options.MaxManifestImportChars = maxImportChars;
        }

        if (t.FirstReadMaxLines is int firstLines && !IsHigher("ToolSchema:FirstReadMaxLines"))
        {
            options.FirstReadMaxLines = firstLines;
        }

        if (t.FirstReadMaxChars is int firstChars && !IsHigher("ToolSchema:FirstReadMaxChars"))
        {
            options.FirstReadMaxChars = firstChars;
        }

        if (t.FirstReadUnwindowedMaxLines is int unwindowed
            && !IsHigher("ToolSchema:FirstReadUnwindowedMaxLines"))
        {
            options.FirstReadUnwindowedMaxLines = unwindowed;
        }

        if (t.SearchSentinelMaxChars is int sentinel && !IsHigher("ToolSchema:SearchSentinelMaxChars"))
        {
            options.SearchSentinelMaxChars = sentinel;
        }

        bool IsHigher(string key) => isHigherPriority?.Invoke(key) == true;
    }
}
