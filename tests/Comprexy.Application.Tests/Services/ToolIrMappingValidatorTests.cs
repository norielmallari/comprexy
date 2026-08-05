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

    [Fact]
    public void GetReplacedClientToolNames_HidesShellBackend()
    {
        var document = new ToolIrMappingDocument
        {
            SchemaHash = "h",
            ClientCapabilities =
            [
                Cap("read", ToolIrCapabilities.FileReadRaw),
                Cap("Shell", ToolIrCapabilities.ShellBackend),
                Cap("write", ToolIrCapabilities.OtherFile)
            ],
            Bindings =
            [
                new ToolIrBinding
                {
                    ComprexyTool = ToolSchemaConstants.FileRangeToolName,
                    PrimaryClientTool = "read",
                    Strategy = ToolIrStrategies.Direct,
                    ArgMap = new Dictionary<string, string>(StringComparer.Ordinal) { ["path"] = "path" }
                },
                new ToolIrBinding
                {
                    ComprexyTool = ToolSchemaConstants.ShellToolName,
                    PrimaryClientTool = "Shell",
                    Strategy = ToolIrStrategies.Direct,
                    ArgMap = new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = "command" }
                }
            ]
        };

        var hidden = ToolIrMappingValidator.GetReplacedClientToolNames(document);

        Assert.Contains("read", hidden);
        Assert.Contains("Shell", hidden);
        Assert.DoesNotContain("write", hidden);
    }

    [Fact]
    public void Validate_ShellBinding_RequiresDirectStrategy()
    {
        const string hash = "abc";
        var json = MappingJson(
            hash,
            [
                Capability("Read"),
                Capability("Shell", "SHELL_BACKEND")
            ],
            [
                new
                {
                    comprexy_tool = "comprexy_read_file_range",
                    primary_client_tool = "Read",
                    strategy = "read_then_slice",
                    arg_map = new { path = "path" }
                },
                new
                {
                    comprexy_tool = "comprexy_shell",
                    primary_client_tool = "Shell",
                    strategy = "read_then_slice",
                    arg_map = new { command = "command" }
                }
            ]);

        var result = ToolIrMappingValidator.Validate(json, Catalog, hash);

        Assert.False(result.IsValid);
        Assert.Contains("requires strategy 'direct'", result.Error);
    }

    [Fact]
    public void Validate_ShellBinding_AcceptsDirect()
    {
        const string hash = "abc";
        var json = MappingJson(
            hash,
            [
                Capability("Read"),
                Capability("Shell", "SHELL_BACKEND")
            ],
            [
                new
                {
                    comprexy_tool = "comprexy_read_file_range",
                    primary_client_tool = "Read",
                    strategy = "read_then_slice",
                    arg_map = new { path = "path" }
                },
                new
                {
                    comprexy_tool = "comprexy_shell",
                    primary_client_tool = "Shell",
                    strategy = "direct",
                    arg_map = new { command = "command" }
                }
            ]);

        var result = ToolIrMappingValidator.Validate(json, Catalog, hash);

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void Validate_ReportsEveryBindingError_NotJustTheFirst()
    {
        const string hash = "abc";
        var json = MappingJson(
            hash,
            [
                Capability("Read"),
                Capability("Shell", "SHELL_BACKEND")
            ],
            [
                new
                {
                    comprexy_tool = "comprexy_read_file_search",
                    primary_client_tool = "Read",
                    strategy = "direct",
                    arg_map = new { query = "pattern" }
                },
                new
                {
                    comprexy_tool = "comprexy_shell",
                    primary_client_tool = "Shell",
                    strategy = "read_then_slice",
                    arg_map = new { command = "command" }
                }
            ]);

        var result = ToolIrMappingValidator.Validate(json, Catalog, hash);

        Assert.False(result.IsValid);
        Assert.Contains("comprexy_read_file_search", result.Error);
        Assert.Contains("requires strategy 'direct'", result.Error);
    }

    [Fact]
    public void Validate_WhenCapabilityMismatch_NamesRebindCandidates()
    {
        const string hash = "abc";
        var catalog = new HashSet<string>(StringComparer.Ordinal) { "read", "glob" };
        var json = MappingJson(
            hash,
            [
                Capability("read"),
                Capability("glob", "FILE_SEARCH_BACKEND")
            ],
            [
                new
                {
                    comprexy_tool = "comprexy_dir_list",
                    primary_client_tool = "read",
                    strategy = "direct",
                    arg_map = new { path = "filePath" }
                }
            ]);

        var result = ToolIrMappingValidator.Validate(json, catalog, hash);

        Assert.False(result.IsValid);
        Assert.Contains("Rebind to one of: glob (FILE_SEARCH_BACKEND)", result.Error);
    }

    [Fact]
    public void Validate_WhenNoCompatibleCandidate_TellsMapperToOmitBinding()
    {
        const string hash = "abc";
        var catalog = new HashSet<string>(StringComparer.Ordinal) { "read" };
        var json = MappingJson(
            hash,
            [Capability("read")],
            [
                new
                {
                    comprexy_tool = "comprexy_dir_list",
                    primary_client_tool = "read",
                    strategy = "direct",
                    arg_map = new { path = "filePath" }
                }
            ]);

        var result = ToolIrMappingValidator.Validate(json, catalog, hash);

        Assert.False(result.IsValid);
        Assert.Contains("omit this binding", result.Error);
    }

    [Fact]
    public void TrySalvage_DropsInvalidBinding_AndKeepsTheRest()
    {
        const string hash = "abc";
        var catalog = new HashSet<string>(StringComparer.Ordinal) { "read", "grep", "glob", "bash" };
        var json = MappingJson(
            hash,
            [
                Capability("read"),
                Capability("grep", "FILE_SEARCH_BACKEND"),
                Capability("glob", "FILE_SEARCH_BACKEND"),
                Capability("bash", "SHELL_BACKEND")
            ],
            [
                new
                {
                    comprexy_tool = "comprexy_read_file_range",
                    primary_client_tool = "read",
                    strategy = "direct",
                    arg_map = new { path = "filePath" }
                },
                new
                {
                    comprexy_tool = "comprexy_read_file_search",
                    primary_client_tool = "grep",
                    strategy = "direct",
                    arg_map = new { query = "pattern" }
                },
                new
                {
                    comprexy_tool = "comprexy_dir_list",
                    primary_client_tool = "read",
                    strategy = "direct",
                    arg_map = new { path = "filePath" }
                },
                new
                {
                    comprexy_tool = "comprexy_shell",
                    primary_client_tool = "bash",
                    strategy = "direct",
                    arg_map = new { command = "command" }
                }
            ]);

        var result = ToolIrMappingValidator.TrySalvage(json, catalog, hash);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(["comprexy_dir_list"], result.DroppedBindings);
        Assert.DoesNotContain(
            result.Document!.Bindings,
            binding => binding.ComprexyTool == "comprexy_dir_list");
        Assert.Equal(3, result.Document.Bindings.Count);
    }

    [Fact]
    public void TrySalvage_RefusesWhenDropWouldStrandAReplacedCapability()
    {
        const string hash = "abc";
        var catalog = new HashSet<string>(StringComparer.Ordinal) { "read", "bash" };
        var json = MappingJson(
            hash,
            [
                Capability("read"),
                Capability("bash", "SHELL_BACKEND")
            ],
            [
                new
                {
                    comprexy_tool = "comprexy_read_file_range",
                    primary_client_tool = "read",
                    strategy = "direct",
                    arg_map = new { path = "filePath" }
                },
                new
                {
                    comprexy_tool = "comprexy_shell",
                    primary_client_tool = "bash",
                    strategy = "read_then_slice",
                    arg_map = new { command = "command" }
                }
            ]);

        var result = ToolIrMappingValidator.TrySalvage(json, catalog, hash);

        Assert.False(result.IsValid);
        Assert.Contains("SHELL_BACKEND", result.Error);
    }

    [Fact]
    public void TrySalvage_RefusesDocumentLevelFailure()
    {
        const string hash = "abc";
        var json = MappingJson(hash, [Capability("Read")]);

        var result = ToolIrMappingValidator.TrySalvage(json, Catalog, "different-hash");

        Assert.False(result.IsValid);
        Assert.Contains("schema_hash mismatch", result.Error);
    }

    [Fact]
    public void VirtualToolRegistry_IncludesFileAndShellFamilies()
    {
        Assert.True(VirtualToolRegistry.IsVirtual(ToolSchemaConstants.FileRangeToolName));
        Assert.True(VirtualToolRegistry.IsVirtual(ToolSchemaConstants.ShellToolName));
        Assert.Contains(ToolIrCapabilities.ShellBackend, VirtualToolRegistry.ReplacedCapabilities);
        Assert.Contains(
            ToolSchemaConstants.ShellToolName,
            VirtualToolRegistry.NamesInFamily(VirtualToolFamilies.Shell));
        Assert.Equal(
            ToolIrVirtualToolDefinitions.ShellWireJson.Trim(),
            VirtualToolRegistry.GetWireJson(ToolSchemaConstants.ShellToolName).Trim());
    }

    [Theory]
    [InlineData("ReadLints")]
    [InlineData("TodoWrite")]
    [InlineData("Task")]
    public void Validate_WhenVirtualPrimaryIsDenylistStubOrTask_IsInvalid(string primary)
    {
        const string hash = "abc";
        var catalog = new HashSet<string>(StringComparer.Ordinal) { "ReadFile", primary };
        var json = MappingJson(
            hash,
            [
                Capability("ReadFile"),
                Capability(primary, "NON_FILE")
            ],
            [
                new
                {
                    comprexy_tool = "comprexy_read_file_range",
                    primary_client_tool = primary,
                    strategy = "read_then_slice",
                    arg_map = new { path = "path" }
                }
            ]);

        var result = ToolIrMappingValidator.Validate(json, catalog, hash);

        Assert.False(result.IsValid);
        Assert.Contains(primary, result.Error);
        Assert.Contains("NON_FILE", result.Error);
        Assert.Contains("FILE_READ_RAW", result.Error);
    }

    [Fact]
    public void Validate_MafIdeBandCatalog_AcceptsNonFileStubsAndTask_BindsOnlyRealBackends()
    {
        const string hash = "maf-ide-band";
        string[] denylist =
        [
            "ReadLints",
            "TodoWrite",
            "AwaitShell",
            "UpdateCurrentStep",
            "EditNotebook",
            "SwitchMode",
            "agent_manager",
            "agent_manager_models",
            "background_process",
            "kilo_local_recall"
        ];

        var catalog = new HashSet<string>(StringComparer.Ordinal)
        {
            "ReadFile",
            "SearchFiles",
            "ListDirectory",
            "RunShellCommand",
            "WriteFile",
            "EditFile",
            "Task"
        };
        foreach (var name in denylist)
        {
            catalog.Add(name);
        }

        var capabilities = new List<object>
        {
            Capability("ReadFile"),
            Capability("SearchFiles", "FILE_SEARCH_BACKEND"),
            Capability("ListDirectory", "DIRECTORY_LIST_BACKEND"),
            Capability("RunShellCommand", "SHELL_BACKEND"),
            Capability("WriteFile", "NON_FILE"),
            Capability("EditFile", "NON_FILE"),
            Capability("Task", "NON_FILE")
        };
        foreach (var name in denylist)
        {
            capabilities.Add(Capability(name, "NON_FILE"));
        }

        var json = MappingJson(
            hash,
            capabilities.ToArray(),
            [
                new
                {
                    comprexy_tool = "comprexy_read_file_range",
                    primary_client_tool = "ReadFile",
                    strategy = "read_then_slice",
                    arg_map = new { path = "path" }
                },
                new
                {
                    comprexy_tool = "comprexy_read_file_manifest",
                    primary_client_tool = "ReadFile",
                    strategy = "direct",
                    arg_map = new { path = "path" }
                },
                new
                {
                    comprexy_tool = "comprexy_read_file_search",
                    primary_client_tool = "SearchFiles",
                    strategy = "direct",
                    arg_map = new { query = "query" }
                },
                new
                {
                    comprexy_tool = "comprexy_dir_list",
                    primary_client_tool = "ListDirectory",
                    strategy = "direct",
                    arg_map = new { path = "path" }
                },
                new
                {
                    comprexy_tool = "comprexy_shell",
                    primary_client_tool = "RunShellCommand",
                    strategy = "direct",
                    arg_map = new { command = "command" }
                }
            ]);

        var result = ToolIrMappingValidator.Validate(json, catalog, hash);

        Assert.True(result.IsValid, result.Error);
        Assert.NotNull(result.Document);
        Assert.All(
            result.Document!.Bindings,
            binding => Assert.DoesNotContain(
                binding.PrimaryClientTool,
                denylist.Append("Task"),
                StringComparer.Ordinal));
        Assert.Contains(
            result.Document.ClientCapabilities,
            c => c.ClientTool == "Task" && c.Capability == "NON_FILE");
        Assert.All(
            denylist,
            name => Assert.Contains(
                result.Document.ClientCapabilities,
                c => c.ClientTool == name && c.Capability == "NON_FILE"));
    }

    private static ToolIrClientCapability Cap(string tool, string capability) => new()
    {
        ClientTool = tool,
        Capability = capability,
        Risk = "low",
        Supports = new ToolIrSupports { Path = true }
    };
}
