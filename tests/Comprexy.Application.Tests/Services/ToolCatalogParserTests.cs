using System.Text.Json;
using Comprexy.Application.Services;

namespace Comprexy.Application.Tests.Services;

public class ToolCatalogParserTests
{
    private readonly ToolCatalogParser _parser = new();

    [Fact]
    public void TryParse_DerivesCompactEntriesWithRequiredFields()
    {
        using var document = JsonDocument.Parse("""
            {
              "tools": [
                {
                  "type": "function",
                  "function": {
                    "name": "beta_tool",
                    "description": "Beta does things.",
                    "parameters": {
                      "type": "object",
                      "required": ["query"]
                    }
                  }
                },
                {
                  "type": "function",
                  "function": {
                    "name": "alpha_tool",
                    "description": "Alpha does things.",
                    "parameters": {
                      "type": "object",
                      "required": ["id", "name"]
                    }
                  }
                }
              ]
            }
            """);

        var parsed = _parser.TryParse(document.RootElement);

        Assert.NotNull(parsed);
        Assert.Equal(2, parsed!.CompactEntries.Count);
        Assert.Contains(parsed.CompactEntries, e => e.Name == "beta_tool" && e.Required.SequenceEqual(["query"]));
        Assert.Contains(parsed.CompactEntries, e => e.Name == "alpha_tool" && e.Required.SequenceEqual(["id", "name"]));
        Assert.False(parsed.HasMetaToolNameCollision);
    }

    [Fact]
    public void TryParse_UsesStubDescriptionWhenMissing()
    {
        using var document = JsonDocument.Parse("""
            {
              "tools": [
                {
                  "type": "function",
                  "function": {
                    "name": "search",
                    "parameters": { "type": "object" }
                  }
                }
              ]
            }
            """);

        var parsed = _parser.TryParse(document.RootElement);

        Assert.NotNull(parsed);
        Assert.Single(parsed!.CompactEntries);
        Assert.Equal("Tool search.", parsed.CompactEntries[0].Description);
    }

    [Fact]
    public void TryParse_ProducesStableCatalogHashForSameToolsPayload()
    {
        const string payload = """
            {
              "tools": [
                {
                  "type": "function",
                  "function": {
                    "name": "lookup",
                    "description": "Look up a value.",
                    "parameters": { "type": "object", "required": ["key"] }
                  }
                }
              ]
            }
            """;

        using var first = JsonDocument.Parse(payload);
        using var second = JsonDocument.Parse(payload);

        var firstParsed = _parser.TryParse(first.RootElement);
        var secondParsed = _parser.TryParse(second.RootElement);

        Assert.NotNull(firstParsed);
        Assert.NotNull(secondParsed);
        Assert.Equal(firstParsed!.CatalogHash, secondParsed!.CatalogHash);
        Assert.False(string.IsNullOrWhiteSpace(firstParsed.CatalogHash));
    }

    [Fact]
    public void TryParse_DetectsMetaToolNameCollision()
    {
        using var document = JsonDocument.Parse("""
            {
              "tools": [
                {
                  "type": "function",
                  "function": {
                    "name": "get_tool_definition",
                    "description": "Client-defined meta tool.",
                    "parameters": { "type": "object" }
                  }
                },
                {
                  "type": "function",
                  "function": {
                    "name": "lookup",
                    "description": "Look up a value.",
                    "parameters": { "type": "object" }
                  }
                }
              ]
            }
            """);

        var parsed = _parser.TryParse(document.RootElement);

        Assert.NotNull(parsed);
        Assert.True(parsed!.HasMetaToolNameCollision);
    }

    [Fact]
    public void BuildCompactIndexJson_OrdersByNameAndIncludesRequired()
    {
        var entries = new[]
        {
            new CompactToolEntry("zebra", "Tool zebra.", ["z"]),
            new CompactToolEntry("alpha", "Tool alpha.", ["a", "b"])
        };

        var json = _parser.BuildCompactIndexJson(entries);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(2, document.RootElement.GetArrayLength());
        Assert.Equal("alpha", document.RootElement[0].GetProperty("name").GetString());
        Assert.Equal("zebra", document.RootElement[1].GetProperty("name").GetString());
        var required = document.RootElement[0].GetProperty("required").EnumerateArray()
            .Select(e => e.GetString()!)
            .ToArray();
        Assert.Equal(["a", "b"], required);
    }
}
