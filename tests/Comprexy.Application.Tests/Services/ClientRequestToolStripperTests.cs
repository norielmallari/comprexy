using System.Text.Json;
using Comprexy.Application.Services;

namespace Comprexy.Application.Tests.Services;

public sealed class ClientRequestToolStripperTests
{
    [Fact]
    public void ForInlineWrapUp_KeepsTools_SetsToolChoiceNone()
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

        var shaped = ClientRequestToolStripper.ForInlineWrapUp(document.RootElement.Clone());

        Assert.NotNull(shaped);
        Assert.Equal("m", shaped!.Value.GetProperty("model").GetString());
        Assert.Equal(0.2, shaped.Value.GetProperty("temperature").GetDouble());
        Assert.True(shaped.Value.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
        Assert.True(shaped.Value.TryGetProperty("tools", out var tools));
        Assert.Equal(JsonValueKind.Array, tools.ValueKind);
        Assert.Equal("none", shaped.Value.GetProperty("tool_choice").GetString());
        Assert.True(shaped.Value.TryGetProperty("functions", out _));
        Assert.Equal("none", shaped.Value.GetProperty("function_call").GetString());
        Assert.True(shaped.Value.GetProperty("parallel_tool_calls").GetBoolean());
    }

    [Fact]
    public void ForInlineWrapUp_WhenToolsPresentWithoutChoice_AddsNone()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "model": "m",
              "tools": [{ "type": "function", "function": { "name": "lookup" } }]
            }
            """);

        var shaped = ClientRequestToolStripper.ForInlineWrapUp(document.RootElement.Clone());

        Assert.NotNull(shaped);
        Assert.True(shaped!.Value.TryGetProperty("tools", out _));
        Assert.Equal("none", shaped.Value.GetProperty("tool_choice").GetString());
    }

    [Fact]
    public void ForInlineWrapUp_WhenAlreadyNone_ReturnsSameInstance()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "model": "m",
              "tools": [{ "type": "function", "function": { "name": "lookup" } }],
              "tool_choice": "none"
            }
            """);
        var original = document.RootElement.Clone();

        var shaped = ClientRequestToolStripper.ForInlineWrapUp(original);

        Assert.True(shaped.HasValue);
        Assert.True(JsonElement.DeepEquals(original, shaped!.Value));
    }

    [Fact]
    public void ForInlineWrapUp_WhenNoToolKeys_ReturnsSameInstance()
    {
        using var document = JsonDocument.Parse("""{"model":"m","temperature":0.1}""");
        var original = document.RootElement.Clone();

        var shaped = ClientRequestToolStripper.ForInlineWrapUp(original);

        Assert.True(shaped.HasValue);
        Assert.True(JsonElement.DeepEquals(original, shaped!.Value));
    }

    [Fact]
    public void ForInlineWrapUp_Null_ReturnsNull()
    {
        Assert.Null(ClientRequestToolStripper.ForInlineWrapUp(null));
    }

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
        Assert.False(stripped.Value.TryGetProperty("tools", out _));
        Assert.False(stripped.Value.TryGetProperty("tool_choice", out _));
    }
}
