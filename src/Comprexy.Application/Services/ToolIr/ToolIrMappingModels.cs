using System.Text.Json;
using System.Text.Json.Serialization;

namespace Comprexy.Application.Services.ToolIr;

public sealed class ToolIrMappingDocument
{
    [JsonPropertyName("schema_hash")]
    public string SchemaHash { get; set; } = string.Empty;

    [JsonPropertyName("client_capabilities")]
    public List<ToolIrClientCapability> ClientCapabilities { get; set; } = [];

    [JsonPropertyName("bindings")]
    public List<ToolIrBinding> Bindings { get; set; } = [];
}

public sealed class ToolIrClientCapability
{
    [JsonPropertyName("client_tool")]
    public string ClientTool { get; set; } = string.Empty;

    [JsonPropertyName("capability")]
    public string Capability { get; set; } = string.Empty;

    [JsonPropertyName("risk")]
    public string Risk { get; set; } = string.Empty;

    [JsonPropertyName("supports")]
    public ToolIrSupports Supports { get; set; } = new();
}

public sealed class ToolIrSupports
{
    [JsonPropertyName("path")]
    public bool Path { get; set; }

    [JsonPropertyName("offset")]
    public bool Offset { get; set; }

    [JsonPropertyName("limit")]
    public bool Limit { get; set; }

    [JsonPropertyName("query")]
    public bool Query { get; set; }
}

public sealed class ToolIrBinding
{
    [JsonPropertyName("comprexy_tool")]
    public string ComprexyTool { get; set; } = string.Empty;

    [JsonPropertyName("primary_client_tool")]
    public string PrimaryClientTool { get; set; } = string.Empty;

    [JsonPropertyName("strategy")]
    public string Strategy { get; set; } = string.Empty;

    [JsonPropertyName("arg_map")]
    public Dictionary<string, string>? ArgMap { get; set; }

    /// <summary>
    /// Client parameter name → JSON literal. Applied when the mapped IR value is absent.
    /// Keys must exist on the primary client tool schema (not IR names).
    /// </summary>
    [JsonPropertyName("defaults")]
    public Dictionary<string, JsonElement>? Defaults { get; set; }
}

public static class ToolIrStrategies
{
    public const string Direct = "direct";
    public const string ReadThenSlice = "read_then_slice";

    public static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        Direct,
        ReadThenSlice
    };
}

public static class ToolIrCapabilities
{
    public const string FileReadRaw = "FILE_READ_RAW";
    public const string FileSearchBackend = "FILE_SEARCH_BACKEND";
    public const string DirectoryListBackend = "DIRECTORY_LIST_BACKEND";
    public const string FileMetadata = "FILE_METADATA";
    public const string OtherFile = "OTHER_FILE";
    public const string NonFile = "NON_FILE";

    public static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        FileReadRaw,
        FileSearchBackend,
        DirectoryListBackend,
        FileMetadata,
        OtherFile,
        NonFile
    };

    /// <summary>
    /// Capabilities whose client tools are replaced by Virtual <c>comprexy_*</c> file tools
    /// and must be hidden from the model-facing catalog. <see cref="OtherFile"/> /
    /// <see cref="FileMetadata"/> / <see cref="NonFile"/> stay as full-schema passthrough
    /// unless they are also a binding primary.
    /// </summary>
    public static readonly HashSet<string> ReplacedByVirtualTools = new(StringComparer.Ordinal)
    {
        FileReadRaw,
        FileSearchBackend,
        DirectoryListBackend
    };

    /// <summary>Allowed primary-tool capabilities per Virtual file tool.</summary>
    public static IReadOnlySet<string>? AllowedForVirtualTool(string comprexyTool) =>
        comprexyTool switch
        {
            ToolSchemaConstants.FileManifestToolName => ManifestCapabilities,
            ToolSchemaConstants.FileRangeToolName => FileReadCapabilities,
            ToolSchemaConstants.FileSearchToolName => SearchCapabilities,
            ToolSchemaConstants.DirListToolName => DirListCapabilities,
            _ => null
        };

    private static readonly HashSet<string> ManifestCapabilities = new(StringComparer.Ordinal)
    {
        FileReadRaw,
        FileMetadata
    };

    private static readonly HashSet<string> FileReadCapabilities = new(StringComparer.Ordinal)
    {
        FileReadRaw
    };

    private static readonly HashSet<string> SearchCapabilities = new(StringComparer.Ordinal)
    {
        FileSearchBackend
    };

    private static readonly HashSet<string> DirListCapabilities = new(StringComparer.Ordinal)
    {
        DirectoryListBackend,
        FileSearchBackend
    };
}
