using System.Text.Json;
using Comprexy.Application.Services;

namespace Comprexy.Application.Tests.Services;

public sealed class ClientRequestToolStripperTests
{
    [Fact]
    public void WithoutTools_RemovesToolRelatedKeys_PreservesOtherFields()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "model": "m",
              "temperature": 0.2,
              "tools": [{ "type": "function", "function": { "name": "lookup" } }],
              "tool_choice": "auto",
              "functions": [{ "name": "legacy" }],
              "function_call": "auto",
              "parallel_tool_calls": true,
              "chat_template_kwargs": { "enable_thinking": true }
            }
            """);

        var stripped = ClientRequestToolStripper.WithoutTools(document.RootElement.Clone());

        Assert.NotNull(stripped);
        Assert.Equal("m", stripped!.Value.GetProperty("model").GetString());
        Assert.Equal(0.2, stripped.Value.GetProperty("temperature").GetDouble());
        Assert.True(stripped.Value.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
        Assert.False(stripped.Value.TryGetProperty("tools", out _));
        Assert.False(stripped.Value.TryGetProperty("tool_choice", out _));
        Assert.False(stripped.Value.TryGetProperty("functions", out _));
        Assert.False(stripped.Value.TryGetProperty("function_call", out _));
        Assert.False(stripped.Value.TryGetProperty("parallel_tool_calls", out _));
    }

    [Fact]
    public void WithoutTools_WhenNoToolKeys_ReturnsSameInstance()
    {
        using var document = JsonDocument.Parse("""{"model":"m","temperature":0.1}""");
        var original = document.RootElement.Clone();

        var stripped = ClientRequestToolStripper.WithoutTools(original);

        Assert.True(stripped.HasValue);
        Assert.True(JsonElement.DeepEquals(original, stripped!.Value));
    }

    [Fact]
    public void WithoutTools_Null_ReturnsNull()
    {
        Assert.Null(ClientRequestToolStripper.WithoutTools(null));
    }
}
