using Comprexy.Domain.Enums;

namespace Comprexy.Application.Configuration;

public class ToolSchemaOptions
{
    public const string SectionName = "ToolSchema";

    public ToolSchemaMode Mode { get; set; } = ToolSchemaMode.Virtual;

    /// <summary>Mapper LLM retries on invalid MappingJson (total attempts = 1 + this value).</summary>
    public int MappingMaxRetries { get; set; } = 2;

    /// <summary>Max lines returned by <c>comprexy_read_file_range</c> when <c>end_line</c> is set (truncated when capped).</summary>
    public int MaxRangeLines { get; set; } = 250;

    /// <summary>Max search hits returned by <c>comprexy_read_file_search</c>.</summary>
    public int MaxSearchMatches { get; set; } = 40;

    /// <summary>Max directory entries returned by <c>comprexy_dir_list</c>.</summary>
    public int MaxDirListEntries { get; set; } = 200;

    /// <summary>Max characters of native shell output kept in a distilled IR observation.</summary>
    public int MaxShellObservationChars { get; set; } = 4000;

    /// <summary>Max characters of native passthrough-family output kept in a distilled IR observation.</summary>
    public int MaxPassthroughObservationChars { get; set; } = 4000;

    /// <summary>Max characters kept in a search-match preview.</summary>
    public int MaxSearchPreviewChars { get; set; } = 200;

    /// <summary>Max import hints returned by <c>comprexy_read_file_manifest</c>.</summary>
    public int MaxManifestImports { get; set; } = 20;

    /// <summary>Max symbol hints returned by <c>comprexy_read_file_manifest</c>.</summary>
    public int MaxManifestSymbols { get; set; } = 30;

    /// <summary>Max characters kept per import hint in a file manifest.</summary>
    public int MaxManifestImportChars { get; set; } = 160;

    /// <summary>
    /// Max lines returned by an unwindowed first read (<c>end_line</c> omitted).
    /// Explicitly windowed reads still use <see cref="MaxRangeLines"/>.
    /// </summary>
    public int FirstReadMaxLines { get; set; } = 400;

    /// <summary>
    /// Max characters of emitted <c>content</c> on an unwindowed first read (<c>end_line</c> omitted).
    /// Binds alongside <see cref="FirstReadMaxLines"/>; whichever cuts first sets <c>truncated</c>.
    /// </summary>
    public int FirstReadMaxChars { get; set; } = 60000;

    /// <summary>
    /// When a complete cached manifest reports more than this many lines, an unwindowed first read
    /// falls back to a windowed <c>direct</c> request of <see cref="FirstReadMaxLines"/> lines
    /// instead of pulling the whole file.
    /// </summary>
    public int FirstReadUnwindowedMaxLines { get; set; } = 2000;

    /// <summary>
    /// Max payload length (chars) for the plain-text search sentinel rule.
    /// Longer unstructured output that lacks <c>path:line:</c> stays preview-only with <c>parse_mode=unstructured</c>.
    /// </summary>
    public int SearchSentinelMaxChars { get; set; } = 400;

    /// <summary>In-memory file-body cache TTL.</summary>
    public TimeSpan FileCacheAbsoluteExpiration { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>Max cached file bodies (each entry size 1).</summary>
    public int FileCacheSizeLimit { get; set; } = 256;

    /// <summary>TTL for abandoned pending IR↔client call-id map entries.</summary>
    public TimeSpan CallIdMapPendingAbsoluteExpiration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Max conversations retained in the process-local call-id map.</summary>
    public int CallIdMapMaxConversations { get; set; } = 1024;

    /// <summary>
    /// Client tool names excluded from the model-facing catalog when Virtual Tools is active.
    /// Case-insensitive ordinal match after trim. Still present in inbound catalog hash / mapper input / stored defs.
    /// </summary>
    public List<string> ExcludeFromModelTools { get; set; } = [];

    /// <summary>First-result shape probe / idle learner knobs.</summary>
    public ResultShapeOptions ResultShape { get; set; } = new();

    /// <summary>Normalized exclude names (trimmed, non-empty, de-duped, ordinal ignore-case).</summary>
    public IReadOnlySet<string> GetNormalizedExcludedToolNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ExcludeFromModelTools)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            names.Add(entry.Trim());
        }

        return names;
    }
}

/// <summary>Process-local result-shape store and optional idle learner.</summary>
public class ResultShapeOptions
{
    /// <summary>Max conversations retained in the shape store (always live; probe + store are not learner-gated).</summary>
    public int MaxConversations { get; set; } = 256;

    /// <summary>Max samples retained per ring (anchor / ambiguous) when the learner is enabled.</summary>
    public int MaxSamplesRetained { get; set; } = 4;

    /// <summary>Caps <c>LineLengths</c> per sanitized sample (learner-only memory knob).</summary>
    public int MaxSampleLines { get; set; } = 512;

    /// <summary>Minimum samples before a learn job may enqueue / promote.</summary>
    public int MinSamplesBeforeProposal { get; set; } = 2;

    /// <summary>Max promote attempts per (conversation, client tool) key.</summary>
    public int MaxProposalAttemptsPerKey { get; set; } = 2;

    /// <summary>Bounded learn-queue capacity (<c>DropWrite</c> on overflow).</summary>
    public int LearnQueueCapacity { get; set; } = 64;

    /// <summary>Idle shape learner (disabled by default; never blocks a chat turn).</summary>
    public ShapeLearnerOptions Learner { get; set; } = new();
}

public class ShapeLearnerOptions
{
    /// <summary>When false, no sampling, enqueue, or hosted worker. Default on.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Debounce after the upstream busy counter reaches zero before a learn job runs.</summary>
    public TimeSpan IdleDebounce { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Max promotions retained per conversation.</summary>
    public int MaxPromotionsPerConversation { get; set; } = 8;
}
