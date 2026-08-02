using System.Text.Json;
using Comprexy.Application.Configuration;
using Comprexy.Application.Services;
using Comprexy.Application.Services.ToolIr;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Tests.Services;

public class ToolIrPlannerTests
{
    private static ToolIrPlanner CreatePlanner()
    {
        var options = Options.Create(new ToolSchemaOptions());
        return new ToolIrPlanner(options, new ToolIrFileBodyCache(options));
    }

    private static ToolIrMappingDocument DocumentWithManifestBoundToGlob() =>
        JsonSerializer.Deserialize<ToolIrMappingDocument>(JsonSerializer.Serialize(new
        {
            schema_hash = "h",
            client_capabilities = new object[]
            {
                new
                {
                    client_tool = "Read",
                    capability = "FILE_READ_RAW",
                    risk = "low",
                    supports = new { path = true, offset = false, limit = false, query = false }
                },
                new
                {
                    client_tool = "Glob",
                    capability = "FILE_SEARCH_BACKEND",
                    risk = "low",
                    supports = new { path = true, offset = false, limit = false, query = false }
                }
            },
            bindings = new object[]
            {
                new
                {
                    comprexy_tool = "comprexy_read_file_manifest",
                    primary_client_tool = "Glob",
                    strategy = "direct",
                    arg_map = new { path = "target_directory" }
                }
            }
        }))!;

    [Fact]
    public void Plan_ManifestBoundToGlob_OverridesToRead()
    {
        var planner = CreatePlanner();
        var mapping = DocumentWithManifestBoundToGlob();
        var call = new ParsedToolCall(
            "call_1",
            ToolSchemaConstants.FileManifestToolName,
            """{"path":"/workspace/repo/docs/a.md"}""");

        var items = planner.Plan(Guid.NewGuid(), [call], mapping);

        Assert.Single(items);
        Assert.Equal(ToolIrPlanKind.NativeClientCall, items[0].Kind);
        Assert.Equal("Read", items[0].ClientToolName);
        Assert.Contains("\"path\"", items[0].ClientArgumentsJson);
        Assert.DoesNotContain("target_directory", items[0].ClientArgumentsJson);
    }

    [Fact]
    public void Plan_DirListBoundToPatternPathGlob_UsesDefaultsOnly()
    {
        var planner = CreatePlanner();
        var mapping = JsonSerializer.Deserialize<ToolIrMappingDocument>(JsonSerializer.Serialize(new
        {
            schema_hash = "h",
            client_capabilities = new object[]
            {
                new
                {
                    client_tool = "glob",
                    capability = "FILE_SEARCH_BACKEND",
                    risk = "low",
                    supports = new { path = true, offset = false, limit = false, query = true }
                }
            },
            bindings = new object[]
            {
                new
                {
                    comprexy_tool = "comprexy_dir_list",
                    primary_client_tool = "glob",
                    strategy = "direct",
                    arg_map = new { path = "path" },
                    defaults = new { pattern = "*" }
                }
            }
        }))!;
        var call = new ParsedToolCall(
            "call_2",
            ToolSchemaConstants.DirListToolName,
            """{"path":"/workspace/repo/docs"}""");

        var items = planner.Plan(Guid.NewGuid(), [call], mapping);

        Assert.Single(items);
        Assert.Equal(ToolIrPlanKind.NativeClientCall, items[0].Kind);
        Assert.Equal("glob", items[0].ClientToolName);
        using var args = JsonDocument.Parse(items[0].ClientArgumentsJson!);
        Assert.Equal("*", args.RootElement.GetProperty("pattern").GetString());
        Assert.Equal(
            "/workspace/repo/docs",
            args.RootElement.GetProperty("path").GetString());
        Assert.False(args.RootElement.TryGetProperty("glob_pattern", out _));
        Assert.False(args.RootElement.TryGetProperty("target_directory", out _));
    }

    [Fact]
    public void Plan_DirListBoundToGlobPatternTargetDirectory_UsesDefaultsOnly()
    {
        var planner = CreatePlanner();
        var mapping = JsonSerializer.Deserialize<ToolIrMappingDocument>(JsonSerializer.Serialize(new
        {
            schema_hash = "h",
            client_capabilities = new object[]
            {
                new
                {
                    client_tool = "Glob",
                    capability = "FILE_SEARCH_BACKEND",
                    risk = "low",
                    supports = new { path = true, offset = false, limit = false, query = true }
                }
            },
            bindings = new object[]
            {
                new
                {
                    comprexy_tool = "comprexy_dir_list",
                    primary_client_tool = "Glob",
                    strategy = "direct",
                    arg_map = new { path = "target_directory" },
                    defaults = new { glob_pattern = "*" }
                }
            }
        }))!;
        var call = new ParsedToolCall(
            "call_3",
            ToolSchemaConstants.DirListToolName,
            """{"path":"/tmp/ws"}""");

        var items = planner.Plan(Guid.NewGuid(), [call], mapping);

        using var args = JsonDocument.Parse(items[0].ClientArgumentsJson!);
        Assert.Equal("*", args.RootElement.GetProperty("glob_pattern").GetString());
        Assert.Equal("/tmp/ws", args.RootElement.GetProperty("target_directory").GetString());
        Assert.False(args.RootElement.TryGetProperty("pattern", out _));
        Assert.False(args.RootElement.TryGetProperty("path", out _));
    }

    [Fact]
    public void Plan_IrMappedValueWinsOverDefaultsForSameClientKey()
    {
        var planner = CreatePlanner();
        var mapping = JsonSerializer.Deserialize<ToolIrMappingDocument>(JsonSerializer.Serialize(new
        {
            schema_hash = "h",
            client_capabilities = new object[]
            {
                new
                {
                    client_tool = "glob",
                    capability = "DIRECTORY_LIST_BACKEND",
                    risk = "low",
                    supports = new { path = true, offset = false, limit = false, query = false }
                }
            },
            bindings = new object[]
            {
                new
                {
                    comprexy_tool = "comprexy_dir_list",
                    primary_client_tool = "glob",
                    strategy = "direct",
                    arg_map = new { path = "path" },
                    defaults = new { path = "/from-defaults", pattern = "*" }
                }
            }
        }))!;
        var call = new ParsedToolCall(
            "call_4",
            ToolSchemaConstants.DirListToolName,
            """{"path":"/from-ir"}""");

        var items = planner.Plan(Guid.NewGuid(), [call], mapping);

        using var args = JsonDocument.Parse(items[0].ClientArgumentsJson!);
        Assert.Equal("/from-ir", args.RootElement.GetProperty("path").GetString());
        Assert.Equal("*", args.RootElement.GetProperty("pattern").GetString());
    }

    [Fact]
    public void Plan_Shell_RemapsCommandViaArgMap()
    {
        var planner = CreatePlanner();
        var mapping = JsonSerializer.Deserialize<ToolIrMappingDocument>(JsonSerializer.Serialize(new
        {
            schema_hash = "h",
            client_capabilities = new object[]
            {
                new
                {
                    client_tool = "Shell",
                    capability = "SHELL_BACKEND",
                    risk = "high",
                    supports = new { path = false, offset = false, limit = false, query = false }
                }
            },
            bindings = new object[]
            {
                new
                {
                    comprexy_tool = "comprexy_shell",
                    primary_client_tool = "Shell",
                    strategy = "direct",
                    arg_map = new
                    {
                        command = "command",
                        working_directory = "working_directory",
                        description = "description"
                    }
                }
            }
        }))!;
        var call = new ParsedToolCall(
            "call_shell",
            ToolSchemaConstants.ShellToolName,
            """{"command":"dotnet test","working_directory":"/workspace/repo","description":"run tests"}""");

        var items = planner.Plan(Guid.NewGuid(), [call], mapping);

        Assert.Single(items);
        Assert.Equal(ToolIrPlanKind.NativeClientCall, items[0].Kind);
        Assert.Equal("Shell", items[0].ClientToolName);
        using var args = JsonDocument.Parse(items[0].ClientArgumentsJson!);
        Assert.Equal("dotnet test", args.RootElement.GetProperty("command").GetString());
        Assert.Equal("/workspace/repo", args.RootElement.GetProperty("working_directory").GetString());
        Assert.Equal("run tests", args.RootElement.GetProperty("description").GetString());
    }

    [Fact]
    public void Plan_Shell_MissingCommand_LocalError()
    {
        var planner = CreatePlanner();
        var mapping = JsonSerializer.Deserialize<ToolIrMappingDocument>(JsonSerializer.Serialize(new
        {
            schema_hash = "h",
            client_capabilities = new object[]
            {
                new
                {
                    client_tool = "Shell",
                    capability = "SHELL_BACKEND",
                    risk = "high",
                    supports = new { path = false, offset = false, limit = false, query = false }
                }
            },
            bindings = new object[]
            {
                new
                {
                    comprexy_tool = "comprexy_shell",
                    primary_client_tool = "Shell",
                    strategy = "direct",
                    arg_map = new { command = "command" }
                }
            }
        }))!;
        var call = new ParsedToolCall("call_shell", ToolSchemaConstants.ShellToolName, "{}");

        var items = planner.Plan(Guid.NewGuid(), [call], mapping);

        Assert.Equal(ToolIrPlanKind.LocalObservation, items[0].Kind);
        Assert.Contains("command", items[0].ObservationJson, StringComparison.OrdinalIgnoreCase);
    }

    private static ToolIrMappingDocument DocumentWithReadRange() =>
        JsonSerializer.Deserialize<ToolIrMappingDocument>(JsonSerializer.Serialize(new
        {
            schema_hash = "h",
            client_capabilities = new object[]
            {
                new
                {
                    client_tool = "Read",
                    capability = "FILE_READ_RAW",
                    risk = "low",
                    supports = new { path = true, offset = true, limit = true, query = false }
                }
            },
            bindings = new object[]
            {
                new
                {
                    comprexy_tool = "comprexy_read_file_range",
                    primary_client_tool = "Read",
                    strategy = "direct",
                    arg_map = new { path = "path", start_line = "offset", end_line = "limit" }
                },
                new
                {
                    comprexy_tool = "comprexy_read_file_manifest",
                    primary_client_tool = "Read",
                    strategy = "direct",
                    arg_map = new { path = "path" }
                }
            }
        }))!;

    [Fact]
    public void PlanFileRange_IncompleteCache_RematerializesEvenInPrefix()
    {
        var options = Options.Create(new ToolSchemaOptions());
        var cache = new ToolIrFileBodyCache(options);
        var planner = new ToolIrPlanner(options, cache);
        var conversationId = Guid.NewGuid();
        var body = string.Join('\n', Enumerable.Range(1, 80).Select(i => $"line-{i}")) + "\n";
        cache.Set(conversationId, "docs/a.md", body, bodyComplete: false, totalLineCount: 267);

        var call = new ParsedToolCall(
            "call_1",
            ToolSchemaConstants.FileRangeToolName,
            """{"path":"docs/a.md","start_line":10,"end_line":40}""");
        var items = planner.Plan(conversationId, [call], DocumentWithReadRange());

        Assert.Equal(ToolIrPlanKind.NativeClientCall, items[0].Kind);
    }

    [Fact]
    public void PlanFileRange_CompleteCache_LocalObservation()
    {
        var options = Options.Create(new ToolSchemaOptions());
        var cache = new ToolIrFileBodyCache(options);
        var planner = new ToolIrPlanner(options, cache);
        var conversationId = Guid.NewGuid();
        cache.Set(conversationId, "docs/a.md", "a\nb\nc\nd\n", bodyComplete: true);

        var call = new ParsedToolCall(
            "call_1",
            ToolSchemaConstants.FileRangeToolName,
            """{"path":"docs/a.md","start_line":1,"end_line":2}""");
        var items = planner.Plan(conversationId, [call], DocumentWithReadRange());

        Assert.Equal(ToolIrPlanKind.LocalObservation, items[0].Kind);
        Assert.Contains("file_range", items[0].ObservationJson, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanManifest_IncompleteVsCompleteCache()
    {
        var options = Options.Create(new ToolSchemaOptions());
        var cache = new ToolIrFileBodyCache(options);
        var planner = new ToolIrPlanner(options, cache);
        var conversationId = Guid.NewGuid();
        cache.Set(conversationId, "docs/a.md", "using X;\n", bodyComplete: false);

        var call = new ParsedToolCall(
            "call_m",
            ToolSchemaConstants.FileManifestToolName,
            """{"path":"docs/a.md"}""");
        Assert.Equal(ToolIrPlanKind.NativeClientCall, planner.Plan(conversationId, [call], DocumentWithReadRange())[0].Kind);

        cache.Set(conversationId, "docs/a.md", "using X;\n", bodyComplete: true);
        Assert.Equal(ToolIrPlanKind.LocalObservation, planner.Plan(conversationId, [call], DocumentWithReadRange())[0].Kind);
    }

    [Fact]
    public void PlanFileRange_EndLineOmitted_ReadThenSlice_NoOffsetLimit()
    {
        var options = Options.Create(new ToolSchemaOptions { FirstReadUnwindowedMaxLines = 2000 });
        var cache = new ToolIrFileBodyCache(options);
        var planner = new ToolIrPlanner(options, cache);
        var conversationId = Guid.NewGuid();
        var call = new ParsedToolCall(
            "call_1",
            ToolSchemaConstants.FileRangeToolName,
            """{"path":"docs/a.md","start_line":1}""");
        var items = planner.Plan(conversationId, [call], DocumentWithReadRange());

        Assert.Equal(ToolIrPlanKind.NativeClientCall, items[0].Kind);
        Assert.Equal(ToolIrStrategies.ReadThenSlice, items[0].Mapping!.Strategy);
        using var args = JsonDocument.Parse(items[0].ClientArgumentsJson!);
        Assert.False(args.RootElement.TryGetProperty("offset", out _));
        Assert.False(args.RootElement.TryGetProperty("limit", out _));
    }

    [Fact]
    public void PlanFileRange_EndLineOmitted_HugeManifest_FallsBackToWindowedDirect()
    {
        var options = Options.Create(new ToolSchemaOptions
        {
            FirstReadUnwindowedMaxLines = 10,
            FirstReadMaxLines = 5
        });
        var cache = new ToolIrFileBodyCache(options);
        var planner = new ToolIrPlanner(options, cache);
        var conversationId = Guid.NewGuid();
        var body = string.Join('\n', Enumerable.Range(1, 50).Select(i => $"line-{i}")) + "\n";
        cache.Set(conversationId, "docs/a.md", body, bodyComplete: true, totalLineCount: 50);

        var call = new ParsedToolCall(
            "call_1",
            ToolSchemaConstants.FileRangeToolName,
            """{"path":"docs/a.md","start_line":1}""");
        var items = planner.Plan(conversationId, [call], DocumentWithReadRange());

        Assert.Equal(ToolIrPlanKind.NativeClientCall, items[0].Kind);
        Assert.Equal(ToolIrStrategies.Direct, items[0].Mapping!.Strategy);
        using var args = JsonDocument.Parse(items[0].ClientArgumentsJson!);
        Assert.True(
            args.RootElement.TryGetProperty("offset", out _) ||
            args.RootElement.TryGetProperty("limit", out _));
    }
}
