using Comprexy.Domain.Enums;

namespace Comprexy.Application.Configuration;

public class ToolSchemaOptions
{
    public const string SectionName = "ToolSchema";

    public ToolSchemaMode Mode { get; set; } = ToolSchemaMode.Virtual;

    /// <summary>Mapper LLM retries on invalid MappingJson (total attempts = 1 + this value).</summary>
    public int MappingMaxRetries { get; set; } = 2;

    /// <summary>Max lines returned by <c>comprexy_read_file_range</c> (truncated when capped).</summary>
    public int MaxRangeLines { get; set; } = 250;

    /// <summary>Max search hits returned by <c>comprexy_read_file_search</c>.</summary>
    public int MaxSearchMatches { get; set; } = 40;

    /// <summary>Max directory entries returned by <c>comprexy_dir_list</c>.</summary>
    public int MaxDirListEntries { get; set; } = 200;

    /// <summary>Max characters of native shell output kept in a distilled IR observation.</summary>
    public int MaxShellObservationChars { get; set; } = 4000;

    /// <summary>In-memory file-body cache TTL.</summary>
    public TimeSpan FileCacheAbsoluteExpiration { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>Max cached file bodies (each entry size 1).</summary>
    public int FileCacheSizeLimit { get; set; } = 256;

    /// <summary>TTL for abandoned pending IR↔client call-id map entries.</summary>
    public TimeSpan CallIdMapPendingAbsoluteExpiration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Max conversations retained in the process-local call-id map.</summary>
    public int CallIdMapMaxConversations { get; set; } = 1024;
}
