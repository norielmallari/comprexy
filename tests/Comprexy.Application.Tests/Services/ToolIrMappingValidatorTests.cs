using System.Text.Json;
using Comprexy.Application.Services;
using Comprexy.Application.Services.ToolIr;

namespace Comprexy.Application.Tests.Services;

public class ToolIrMappingValidatorTests
{
    private static readonly IReadOnlySet<string> Catalog = new HashSet<string>(StringComparer.Ordinal)
    {
        "Read",
        "Shell"
    };

    private static string MappingJson(
        string schemaHash,
        object[] capabilities,
        object[]? bindings = null) =>
        JsonSerializer.Serialize(new
        {
            schema_hash = schemaHash,
            client_capabilities = capabilities,
            bindings = bindings ??
            [
                new
                {
                    comprexy_tool = "comprexy_read_file_range",
                    primary_client_tool = "Read",
                    strategy = "read_then_slice",
                    arg_map = new { path = "path" }
                }
            ]
        });

    private static object Capability(string tool, string capability = "FILE_READ_RAW") => new
    {
        client_tool = tool,
        capability,
        risk = "low",
        supports = new { path = true, offset = false, limit = false, query = false }
    };

    private static string GlobDefinition(string patternKey, string pathKey, bool additionalProperties = true) =>
        JsonSerializer.Serialize(new
        {
            type = "function",
            function = new
            {
                name = "glob",
                parameters = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        [patternKey] = new { type = "string" },
                        [pathKey] = new { type = "string" }
                    },
                    required = new[] { patternKey, pathKey },
                    additionalProperties
                }
            }
        });

    [Fact]
    public void Validate_WhenCatalogToolOmitted_IsInvalid()
    {
        const string hash = "abc";
        var json = MappingJson(hash, [Capability("Read")]);

        var result = ToolIrMappingValidator.Validate(json, Catalog, hash);

        Assert.False(result.IsValid);
        Assert.Contains("Missing client_capabilities entry for catalog tool 'Shell'", result.Error);
    }

    [Fact]
    public void Validate_WhenCatalogToolDuplicated_IsInvalid()
    {
        const string hash = "abc";
        var json = MappingJson(
            hash,
            [
                Capability("Read"),
                Capability("Shell", "NON_FILE"),
                Capability("Read")
            ]);

        var result = ToolIrMappingValidator.Validate(json, Catalog, hash);

        Assert.False(result.IsValid);
        Assert.Contains("Duplicate client_capabilities entry for 'Read'", result.Error);
    }

    [Fact]
    public void Validate_WhenEveryCatalogToolExactlyOnce_IsValid()
    {
        const string hash = "abc";
        var json = MappingJson(
            hash,
            [
                Capability("Read"),
                Capability("Shell", "NON_FILE")
            ]);

        var result = ToolIrMappingValidator.Validate(json, Catalog, hash);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Document);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Validate_WhenManifestBoundToGlob_IsInvalid()
    {
        const string hash = "abc";
        var catalog = new HashSet<string>(StringComparer.Ordinal) { "Read", "Glob", "Shell" };
        var json = MappingJson(
            hash,
            [
                Capability("Read"),
                Capability("Glob", "FILE_SEARCH_BACKEND"),
                Capability("Shell", "NON_FILE")
            ],
            [
                new
                {
                    comprexy_tool = "comprexy_read_file_manifest",
                    primary_client_tool = "Glob",
                    strategy = "direct",
                    arg_map = new { path = "target_directory" }
                }
            ]);

        var result = ToolIrMappingValidator.Validate(json, catalog, hash);

        Assert.False(result.IsValid);
        Assert.Contains("comprexy_read_file_manifest", result.Error);
        Assert.Contains("FILE_SEARCH_BACKEND", result.Error);
    }

    [Fact]
    public void Validate_WhenRequiredClientArgUncovered_FailsWithMissingPropertyAndSchemaSnippet()
    {
        const string hash = "abc";
        var catalog = new HashSet<string>(StringComparer.Ordinal) { "glob" };
        var json = MappingJson(
            hash,
            [Capability("glob", "DIRECTORY_LIST_BACKEND")],
            [
                new
                {
                    comprexy_tool = "comprexy_dir_list",
                    primary_client_tool = "glob",
                    strategy = "direct",
                    arg_map = new { path = "path" }
                }
            ]);
        var defs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["glob"] = GlobDefinition("pattern", "path")
        };

        var result = ToolIrMappingValidator.Validate(json, catalog, hash, defs);

        Assert.False(result.IsValid);
        Assert.Contains("pattern", result.Error);
        Assert.Contains("uncovered", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Schema snippet:", result.Error);
    }

    [Fact]
    public void Validate_WhenDefaultsCoverRequired_IsValid()
    {
        const string hash = "abc";
        var catalog = new HashSet<string>(StringComparer.Ordinal) { "glob" };
        var json = MappingJson(
            hash,
            [Capability("glob", "DIRECTORY_LIST_BACKEND")],
            [
                new
                {
                    comprexy_tool = "comprexy_dir_list",
                    primary_client_tool = "glob",
                    strategy = "direct",
                    arg_map = new { path = "path" },
                    defaults = new { pattern = "*" }
                }
            ]);
        var defs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["glob"] = GlobDefinition("pattern", "path")
        };

        var result = ToolIrMappingValidator.Validate(json, catalog, hash, defs);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Document);
        Assert.NotNull(result.Document!.Bindings[0].Defaults);
        Assert.True(result.Document.Bindings[0].Defaults!.ContainsKey("pattern"));
    }

    [Fact]
    public void Validate_WhenAdditionalPropertiesFalse_RejectsUnknownDefaultsKey()
    {
        const string hash = "abc";
        var catalog = new HashSet<string>(StringComparer.Ordinal) { "glob" };
        var json = MappingJson(
            hash,
            [Capability("glob", "DIRECTORY_LIST_BACKEND")],
            [
                new
                {
                    comprexy_tool = "comprexy_dir_list",
                    primary_client_tool = "glob",
                    strategy = "direct",
                    arg_map = new { path = "path" },
                    defaults = new { pattern = "*", unknown_extra = "x" }
                }
            ]);
        var defs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["glob"] = GlobDefinition("pattern", "path", additionalProperties: false)
        };

        var result = ToolIrMappingValidator.Validate(json, catalog, hash, defs);

        Assert.False(result.IsValid);
        Assert.Contains("additionalProperties=false", result.Error);
        Assert.Contains("defaults.unknown_extra", result.Error);
    }

    [Fact]
    public void Validate_WhenAdditionalPropertiesFalse_RejectsUnknownArgMapClientKey()
    {
        const string hash = "abc";
        var catalog = new HashSet<string>(StringComparer.Ordinal) { "glob" };
        var json = MappingJson(
            hash,
            [Capability("glob", "DIRECTORY_LIST_BACKEND")],
            [
                new
                {
                    comprexy_tool = "comprexy_dir_list",
                    primary_client_tool = "glob",
                    strategy = "direct",
                    arg_map = new { path = "path", query = "not_a_real_property" },
                    defaults = new { pattern = "*" }
                }
            ]);
        var defs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["glob"] = GlobDefinition("pattern", "path", additionalProperties: false)
        };

        var result = ToolIrMappingValidator.Validate(json, catalog, hash, defs);

        Assert.False(result.IsValid);
        Assert.Contains("arg_map→not_a_real_property", result.Error);
    }

    [Fact]
    public void GetFileClientToolNames_HidesReplacedBackends_NotOtherFileOrNonFile()
    {
        var document = new ToolIrMappingDocument
        {
            SchemaHash = "h",
            ClientCapabilities =
            [
                Cap("read", ToolIrCapabilities.FileReadRaw),
                Cap("grep", ToolIrCapabilities.FileSearchBackend),
                Cap("glob", ToolIrCapabilities.DirectoryListBackend),
                Cap("write", ToolIrCapabilities.OtherFile),
                Cap("edit", ToolIrCapabilities.OtherFile),
                Cap("stat", ToolIrCapabilities.FileMetadata),
                Cap("bash", ToolIrCapabilities.NonFile)
            ],
            Bindings =
            [
                new ToolIrBinding
                {
                    ComprexyTool = ToolSchemaConstants.FileRangeToolName,
                    PrimaryClientTool = "read",
                    Strategy = ToolIrStrategies.ReadThenSlice,
                    ArgMap = new Dictionary<string, string>(StringComparer.Ordinal) { ["path"] = "path" }
                },
                new ToolIrBinding
                {
                    ComprexyTool = ToolSchemaConstants.FileSearchToolName,
                    PrimaryClientTool = "grep",
                    Strategy = ToolIrStrategies.Direct,
                    ArgMap = new Dictionary<string, string>(StringComparer.Ordinal) { ["query"] = "pattern" }
                },
                new ToolIrBinding
                {
                    ComprexyTool = ToolSchemaConstants.DirListToolName,
                    PrimaryClientTool = "glob",
                    Strategy = ToolIrStrategies.Direct,
                    ArgMap = new Dictionary<string, string>(StringComparer.Ordinal) { ["path"] = "path" }
                }
            ]
        };

        var hidden = ToolIrMappingValidator.GetFileClientToolNames(document);

        Assert.Contains("read", hidden);
        Assert.Contains("grep", hidden);
        Assert.Contains("glob", hidden);
        Assert.DoesNotContain("write", hidden);
        Assert.DoesNotContain("edit", hidden);
        Assert.DoesNotContain("stat", hidden);
        Assert.DoesNotContain("bash", hidden);
    }

    [Fact]
    public void GetFileClientToolNames_IncludesBindingPrimaryEvenWhenFileMetadata()
    {
        var document = new ToolIrMappingDocument
        {
            SchemaHash = "h",
            ClientCapabilities =
            [
                Cap("Stat", ToolIrCapabilities.FileMetadata),
                Cap("write", ToolIrCapabilities.OtherFile)
            ],
            Bindings =
            [
                new ToolIrBinding
                {
                    ComprexyTool = ToolSchemaConstants.FileManifestToolName,
                    PrimaryClientTool = "Stat",
                    Strategy = ToolIrStrategies.Direct,
                    ArgMap = new Dictionary<string, string>(StringComparer.Ordinal) { ["path"] = "path" }
                }
            ]
        };

        var hidden = ToolIrMappingValidator.GetFileClientToolNames(document);

        Assert.Contains("Stat", hidden);
        Assert.DoesNotContain("write", hidden);
    }

    private static ToolIrClientCapability Cap(string tool, string capability) => new()
    {
        ClientTool = tool,
        Capability = capability,
        Risk = "low",
        Supports = new ToolIrSupports { Path = true }
    };
}
