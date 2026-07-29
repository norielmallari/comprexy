namespace Comprexy.Application.Services.ToolIr;

/// <summary>Static descriptors for Virtual IR tools, grouped by family (file, shell, …).</summary>
public sealed record VirtualToolSpec(
    string Name,
    string Family,
    string WireJson,
    IReadOnlySet<string> AllowedPrimaryCapabilities);

public static class VirtualToolFamilies
{
    public const string File = "file";
    public const string Shell = "shell";
}

/// <summary>
/// Registry of model-facing Virtual tools. File and shell families plug in here so
/// orchestrator / validator / planner do not hard-code file-only name lists.
/// </summary>
public static class VirtualToolRegistry
{
    private static readonly VirtualToolSpec[] Specs =
    [
        new(
            ToolSchemaConstants.FileManifestToolName,
            VirtualToolFamilies.File,
            ToolIrVirtualToolDefinitions.FileManifestWireJson,
            new HashSet<string>(StringComparer.Ordinal)
            {
                ToolIrCapabilities.FileReadRaw,
                ToolIrCapabilities.FileMetadata
            }),
        new(
            ToolSchemaConstants.FileRangeToolName,
            VirtualToolFamilies.File,
            ToolIrVirtualToolDefinitions.FileRangeWireJson,
            new HashSet<string>(StringComparer.Ordinal) { ToolIrCapabilities.FileReadRaw }),
        new(
            ToolSchemaConstants.FileSearchToolName,
            VirtualToolFamilies.File,
            ToolIrVirtualToolDefinitions.FileSearchWireJson,
            new HashSet<string>(StringComparer.Ordinal) { ToolIrCapabilities.FileSearchBackend }),
        new(
            ToolSchemaConstants.DirListToolName,
            VirtualToolFamilies.File,
            ToolIrVirtualToolDefinitions.DirListWireJson,
            new HashSet<string>(StringComparer.Ordinal)
            {
                ToolIrCapabilities.DirectoryListBackend,
                ToolIrCapabilities.FileSearchBackend
            }),
        new(
            ToolSchemaConstants.ShellToolName,
            VirtualToolFamilies.Shell,
            ToolIrVirtualToolDefinitions.ShellWireJson,
            new HashSet<string>(StringComparer.Ordinal) { ToolIrCapabilities.ShellBackend })
    ];

    private static readonly Dictionary<string, VirtualToolSpec> ByName =
        Specs.ToDictionary(s => s.Name, StringComparer.Ordinal);

    public static IReadOnlyList<VirtualToolSpec> All { get; } = Specs;

    public static IReadOnlySet<string> VirtualToolNames { get; } =
        Specs.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Client capabilities whose backends are replaced by Virtual IR tools and hidden
    /// from the model-facing catalog (when present on a capability row).
    /// </summary>
    public static IReadOnlySet<string> ReplacedCapabilities { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            ToolIrCapabilities.FileReadRaw,
            ToolIrCapabilities.FileSearchBackend,
            ToolIrCapabilities.DirectoryListBackend,
            ToolIrCapabilities.ShellBackend
        };

    public static bool IsVirtual(string? name) =>
        !string.IsNullOrWhiteSpace(name) && ByName.ContainsKey(name);

    public static bool TryGet(string name, out VirtualToolSpec spec) =>
        ByName.TryGetValue(name, out spec!);

    public static IReadOnlySet<string>? AllowedCapsFor(string comprexyTool) =>
        ByName.TryGetValue(comprexyTool, out var spec) ? spec.AllowedPrimaryCapabilities : null;

    public static string GetWireJson(string toolName)
    {
        if (!ByName.TryGetValue(toolName, out var spec))
        {
            throw new ArgumentOutOfRangeException(nameof(toolName), toolName, "Unknown virtual tool.");
        }

        return spec.WireJson;
    }

    public static IEnumerable<string> NamesInFamily(string family) =>
        Specs.Where(s => string.Equals(s.Family, family, StringComparison.Ordinal))
            .Select(s => s.Name);
}
