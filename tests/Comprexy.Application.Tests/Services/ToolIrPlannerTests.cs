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
            """{"path":"/workspace/repo/personas"}""");

        var items = planner.Plan(Guid.NewGuid(), [call], mapping);

        Assert.Single(items);
        Assert.Equal(ToolIrPlanKind.NativeClientCall, items[0].Kind);
        Assert.Equal("glob", items[0].ClientToolName);
        using var args = JsonDocument.Parse(items[0].ClientArgumentsJson!);
        Assert.Equal("*", args.RootElement.GetProperty("pattern").GetString());
        Assert.Equal(
            "/workspace/repo/personas",
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
}
