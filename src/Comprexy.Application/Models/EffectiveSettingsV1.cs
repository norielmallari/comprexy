using System.Text.Json.Serialization;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Models;

/// <summary>
/// Versioned allowlisted behavior knobs for sticky conversation snapshots and live resolve.
/// Secrets and non-allowlisted config are never included.
/// </summary>
public sealed class EffectiveSettingsV1
{
    public const int SchemaVersion = 1;

    [JsonPropertyName("v")]
    public int V { get; init; } = SchemaVersion;

    public bool PassThrough { get; init; }

    public OptimizationMode OptimizationMode { get; init; } = OptimizationMode.Full;

    public bool StripReasoningContent { get; init; }

    public int SoftLimitTokens { get; init; }

    public int MinTurnsBetweenGenerations { get; init; }

    public int CompressionRetainMessageCount { get; init; }

    public bool DedupeDuplicateFailedEdits { get; init; }

    public string TokenizerEncoding { get; init; } = "cl100k_base";

    public bool CacheAlignmentEnabled { get; init; }

    public int CacheAlignmentMaxConversations { get; init; }

    public bool MetricsEnabled { get; init; }

    public PromptTokenBasis PromptTokenBasis { get; init; } = PromptTokenBasis.ProviderActual;

    public ToolSchemaMode ToolSchemaMode { get; init; } = ToolSchemaMode.Virtual;

    public List<string> ExcludeFromModelTools { get; init; } = [];

    public int MappingMaxRetries { get; init; }

    public int MaxRangeLines { get; init; }

    public int MaxSearchMatches { get; init; }

    public int MaxDirListEntries { get; init; }

    public int MaxShellObservationChars { get; init; }

    public int MaxPassthroughObservationChars { get; init; }

    public int MaxSearchPreviewChars { get; init; }

    public int MaxManifestImports { get; init; }

    public int MaxManifestSymbols { get; init; }

    public int MaxManifestImportChars { get; init; }

    public int FirstReadMaxLines { get; init; }

    public int FirstReadMaxChars { get; init; }

    public int FirstReadUnwindowedMaxLines { get; init; }

    public int SearchSentinelMaxChars { get; init; }

    /// <summary>
    /// Persist BaseSystem for observability. True when not PassThrough.
    /// MonitorOnly captures without mutating the outgoing prompt; PassThrough never captures.
    /// </summary>
    public bool CapturesBaseSystemForObservability => !PassThrough;

    /// <summary>PassThrough or MonitorOnly — same pre-return optimization skip set.</summary>
    public bool SkipsPromptOptimizations =>
        PassThrough || OptimizationMode == OptimizationMode.MonitorOnly;
}
