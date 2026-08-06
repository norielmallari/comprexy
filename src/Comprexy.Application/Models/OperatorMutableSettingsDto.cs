using System.Text.Json.Serialization;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Models;

/// <summary>
/// Allowlisted mutable settings DTO for control-api GET/PUT (section-shaped JSON in SQLite).
/// Unknown / secret keys are rejected at the endpoint.
/// </summary>
public sealed class OperatorMutableSettingsDto
{
    public ProxyMutableDto? Proxy { get; init; }

    public ContextPolicyMutableDto? ContextPolicy { get; init; }

    public CacheAlignmentMutableDto? CacheAlignment { get; init; }

    public MetricsMutableDto? Metrics { get; init; }

    public ToolSchemaMutableDto? ToolSchema { get; init; }
}

public sealed class ProxyMutableDto
{
    public bool? PassThrough { get; init; }

    public OptimizationMode? OptimizationMode { get; init; }

    public bool? StripReasoningContent { get; init; }
}

public sealed class ContextPolicyMutableDto
{
    public int? SoftLimitTokens { get; init; }

    public int? MinTurnsBetweenGenerations { get; init; }

    public int? CompressionRetainMessageCount { get; init; }

    public bool? DedupeDuplicateFailedEdits { get; init; }

    public string? TokenizerEncoding { get; init; }
}

public sealed class CacheAlignmentMutableDto
{
    public bool? Enabled { get; init; }

    public int? MaxConversations { get; init; }
}

public sealed class MetricsMutableDto
{
    public bool? Enabled { get; init; }

    public PromptTokenBasis? PromptTokenBasis { get; init; }
}

public sealed class ToolSchemaMutableDto
{
    public ToolSchemaMode? Mode { get; init; }

    public List<string>? ExcludeFromModelTools { get; init; }

    public int? MappingMaxRetries { get; init; }

    public int? MaxRangeLines { get; init; }

    public int? MaxSearchMatches { get; init; }

    public int? MaxDirListEntries { get; init; }

    public int? MaxShellObservationChars { get; init; }

    public int? MaxPassthroughObservationChars { get; init; }

    public int? MaxSearchPreviewChars { get; init; }

    public int? MaxManifestImports { get; init; }

    public int? MaxManifestSymbols { get; init; }

    public int? MaxManifestImportChars { get; init; }

    public int? FirstReadMaxLines { get; init; }

    public int? FirstReadMaxChars { get; init; }

    public int? FirstReadUnwindowedMaxLines { get; init; }

    public int? SearchSentinelMaxChars { get; init; }
}

/// <summary>GET response envelope with optimistic revision.</summary>
public sealed class OperatorSettingsGetResponse
{
    public long Revision { get; init; }

    public OperatorMutableSettingsDto Settings { get; init; } = new();

    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>PUT body: revision must match current (If-Match / body).</summary>
public sealed class OperatorSettingsPutRequest
{
    public long Revision { get; init; }

    public OperatorMutableSettingsDto Settings { get; init; } = new();
}
