using System.Text.Json;
using Comprexy.Application.Services;

namespace Comprexy.Application.Tests.Services;

public class ToolArgumentValidatorTests
{
    private readonly ToolArgumentValidator _validator = new();

    private const string ParametersSchema = """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string" },
            "limit": { "type": "integer", "minimum": 1 }
          },
          "required": ["query"]
        }
        """;

    [Fact]
    public void Validate_AcceptsArgumentsMatchingSchema()
    {
        var result = _validator.Validate(ParametersSchema, """{"query":"hello","limit":5}""");

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.Details);
    }

    [Fact]
    public void Validate_RejectsMissingRequiredField()
    {
        var result = _validator.Validate(ParametersSchema, """{"limit":5}""");

        Assert.False(result.IsValid);
        Assert.Equal("schema_invalid", result.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Details));
    }

    [Fact]
    public void Validate_RejectsInvalidJsonArguments()
    {
        var result = _validator.Validate(ParametersSchema, """not-json""");

        Assert.False(result.IsValid);
        Assert.Equal("invalid_args", result.ErrorCode);
        Assert.Contains("not valid JSON", result.Details, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExtractParametersSchemaJson_ReadsNestedFunctionParameters()
    {
        const string definition = """
            {
              "type": "function",
              "function": {
                "name": "lookup",
                "parameters": { "type": "object", "required": ["query"] }
              }
            }
            """;

        var schema = _validator.ExtractParametersSchemaJson(definition);

        Assert.NotNull(schema);
        using var document = JsonDocument.Parse(schema!);
        Assert.True(document.RootElement.TryGetProperty("required", out var required));
        Assert.Equal("query", required[0].GetString());
    }

    [Fact]
    public void Validate_CoercesStringifiedObjectProperty_WhenSchemaExpectsObject()
    {
        const string schema = """
            {
              "type": "object",
              "properties": {
                "server": { "type": "string" },
                "toolName": { "type": "string" },
                "arguments": { "type": "object" }
              },
              "required": ["server", "toolName", "arguments"]
            }
            """;

        var result = _validator.Validate(
            schema,
            """{"server":"cursor-ide","toolName":"Shell","arguments":"{\"command\":\"ls\"}"}""");

        Assert.True(result.IsValid);
        Assert.NotNull(result.NormalizedArgumentsJson);

        using var document = JsonDocument.Parse(result.NormalizedArgumentsJson!);
        Assert.Equal(JsonValueKind.Object, document.RootElement.GetProperty("arguments").ValueKind);
        Assert.Equal("ls", document.RootElement.GetProperty("arguments").GetProperty("command").GetString());
    }

    [Fact]
    public void Validate_CoercesStringifiedArrayProperty_WhenSchemaExpectsArray()
    {
        const string schema = """
            {
              "type": "object",
              "properties": {
                "tags": { "type": "array", "items": { "type": "string" } }
              },
              "required": ["tags"]
            }
            """;

        var result = _validator.Validate(schema, """{"tags":"[\"a\",\"b\"]"}""");

        Assert.True(result.IsValid);
        Assert.NotNull(result.NormalizedArgumentsJson);

        using var document = JsonDocument.Parse(result.NormalizedArgumentsJson!);
        var tags = document.RootElement.GetProperty("tags");
        Assert.Equal(JsonValueKind.Array, tags.ValueKind);
        Assert.Equal(2, tags.GetArrayLength());
    }

    [Fact]
    public void Validate_DoesNotCoerceStringifiedObject_WhenSchemaExpectsString()
    {
        const string schema = """
            {
              "type": "object",
              "properties": {
                "payload": { "type": "string" }
              },
              "required": ["payload"]
            }
            """;

        var result = _validator.Validate(schema, """{"payload":"{\"a\":1}"}""");

        Assert.True(result.IsValid);
        using var document = JsonDocument.Parse(result.NormalizedArgumentsJson!);
        Assert.Equal(JsonValueKind.String, document.RootElement.GetProperty("payload").ValueKind);
    }
}

/// <summary>
/// Wire rewrite for coerced tool-call arguments.
/// </summary>
public class ToolCallWireHelperReplaceTests
{
    [Fact]
    public void ReplaceToolCallArguments_RewritesMatchingCallAsJsonString()
    {
        const string assistant = """
            {
              "role": "assistant",
              "tool_calls": [
                {
                  "id": "call_1",
                  "type": "function",
                  "function": {
                    "name": "CallMcpTool",
                    "arguments": "{\"server\":\"x\",\"toolName\":\"Shell\",\"arguments\":\"{\\\"command\\\":\\\"ls\\\"}\"}"
                  }
                }
              ]
            }
            """;

        var rewritten = ToolCallWireHelper.ReplaceToolCallArguments(
            assistant,
            new Dictionary<string, string>
            {
                ["call_1"] = """{"server":"x","toolName":"Shell","arguments":{"command":"ls"}}"""
            });

        Assert.NotNull(rewritten);
        var calls = ToolCallWireHelper.ParseAssistantToolCalls(rewritten);
        Assert.Single(calls);
        using var document = JsonDocument.Parse(calls[0].ArgumentsJson);
        Assert.Equal(JsonValueKind.Object, document.RootElement.GetProperty("arguments").ValueKind);
    }
}

public class ToolCallWireHelperStreamChunkTests
{
    [Theory]
    [InlineData("""{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"c1"}]}}]}""", true)]
    [InlineData("""{"choices":[{"delta":{"tool_calls":[]}}]}""", false)]
    [InlineData("""{"choices":[{"delta":{"content":"hi"}}]}""", false)]
    [InlineData("""{"id":"x"}""", false)]
    [InlineData("""{not-json""", false)]
    [InlineData("[DONE]", false)]
    public void StreamChunkHasToolCalls_ClassifiesFrames(string data, bool expected) =>
        Assert.Equal(expected, ToolCallWireHelper.StreamChunkHasToolCalls(data));
}
