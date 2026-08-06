using System.Text.Json;
using System.Text.Json.Serialization;
using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services.Settings;

/// <summary>
/// Captures allowlisted live options into sticky JSON and deserializes snapshots.
/// </summary>
public static class EffectiveSettingsSerializer
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = false
    };

    public static EffectiveSettingsV1 CaptureFrom(
        IOptionsMonitor<ProxyOptions> proxy,
        IOptionsMonitor<ContextPolicyOptions> contextPolicy,
        IOptionsMonitor<CacheAlignmentOptions> cacheAlignment,
        IOptionsMonitor<MetricsOptions> metrics,
        IOptionsMonitor<ToolSchemaOptions> toolSchema) =>
        CaptureFrom(
            proxy.CurrentValue,
            contextPolicy.CurrentValue,
            cacheAlignment.CurrentValue,
            metrics.CurrentValue,
            toolSchema.CurrentValue);

    public static EffectiveSettingsV1 CaptureFrom(
        ProxyOptions proxy,
        ContextPolicyOptions contextPolicy,
        CacheAlignmentOptions cacheAlignment,
        MetricsOptions metrics,
        ToolSchemaOptions toolSchema) =>
        new()
        {
            V = EffectiveSettingsV1.SchemaVersion,
            PassThrough = proxy.PassThrough,
            OptimizationMode = proxy.OptimizationMode,
            StripReasoningContent = proxy.StripReasoningContent,
            SoftLimitTokens = contextPolicy.SoftLimitTokens,
            MinTurnsBetweenGenerations = contextPolicy.MinTurnsBetweenGenerations,
            CompressionRetainMessageCount = contextPolicy.CompressionRetainMessageCount,
            DedupeDuplicateFailedEdits = contextPolicy.DedupeDuplicateFailedEdits,
            TokenizerEncoding = contextPolicy.TokenizerEncoding,
            CacheAlignmentEnabled = cacheAlignment.Enabled,
            CacheAlignmentMaxConversations = cacheAlignment.MaxConversations,
            MetricsEnabled = metrics.Enabled,
            PromptTokenBasis = metrics.PromptTokenBasis,
            ToolSchemaMode = toolSchema.Mode,
            ExcludeFromModelTools = [.. toolSchema.ExcludeFromModelTools],
            MappingMaxRetries = toolSchema.MappingMaxRetries,
            MaxRangeLines = toolSchema.MaxRangeLines,
            MaxSearchMatches = toolSchema.MaxSearchMatches,
            MaxDirListEntries = toolSchema.MaxDirListEntries,
            MaxShellObservationChars = toolSchema.MaxShellObservationChars,
            MaxPassthroughObservationChars = toolSchema.MaxPassthroughObservationChars,
            MaxSearchPreviewChars = toolSchema.MaxSearchPreviewChars,
            MaxManifestImports = toolSchema.MaxManifestImports,
            MaxManifestSymbols = toolSchema.MaxManifestSymbols,
            MaxManifestImportChars = toolSchema.MaxManifestImportChars,
            FirstReadMaxLines = toolSchema.FirstReadMaxLines,
            FirstReadMaxChars = toolSchema.FirstReadMaxChars,
            FirstReadUnwindowedMaxLines = toolSchema.FirstReadUnwindowedMaxLines,
            SearchSentinelMaxChars = toolSchema.SearchSentinelMaxChars
        };

    public static string Serialize(EffectiveSettingsV1 settings) =>
        JsonSerializer.Serialize(settings, JsonOptions);

    public static EffectiveSettingsV1 Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var settings = JsonSerializer.Deserialize<EffectiveSettingsV1>(json, JsonOptions)
            ?? throw new InvalidOperationException("Effective settings JSON deserialized to null.");
        if (settings.V != EffectiveSettingsV1.SchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported effective settings schema version {settings.V}.");
        }

        return settings;
    }
}
