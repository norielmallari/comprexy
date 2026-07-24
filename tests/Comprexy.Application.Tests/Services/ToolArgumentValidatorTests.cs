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
}
