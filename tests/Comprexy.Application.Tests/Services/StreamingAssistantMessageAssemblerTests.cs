using System.Text.Json;
using Comprexy.Infrastructure.Providers;

namespace Comprexy.Application.Tests.Services;

public class StreamingAssistantMessageAssemblerTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null
    };

    [Fact]
    public void MergeDelta_AccumulatesReasoningContentAndContent()
    {
        var assembler = new StreamingAssistantMessageAssembler();
        Merge(assembler, """{"reasoning_content":"think ","content":null}""");
        Merge(assembler, """{"reasoning_content":"hard","content":"Hello"}""");
        Merge(assembler, """{"content":" world"}""");

        Assert.Equal("Hello world", assembler.Content);

        using var document = JsonDocument.Parse(assembler.BuildMessageJson(SerializerOptions));
        var message = document.RootElement;
        Assert.Equal("assistant", message.GetProperty("role").GetString());
        Assert.Equal("Hello world", message.GetProperty("content").GetString());
        Assert.Equal("think hard", message.GetProperty("reasoning_content").GetString());
    }

    [Fact]
    public void MergeDelta_PreservesToolCallsAndExtensionsTogether()
    {
        var assembler = new StreamingAssistantMessageAssembler();
        Merge(
            assembler,
            """{"reasoning_content":"plan","tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"lookup","arguments":""}}]}""");
        Merge(
            assembler,
            """{"tool_calls":[{"index":0,"function":{"arguments":"{\"q\":\"x\"}"}}]}""");

        using var document = JsonDocument.Parse(assembler.BuildMessageJson(SerializerOptions));
        var message = document.RootElement;
        Assert.Equal("plan", message.GetProperty("reasoning_content").GetString());
        Assert.Equal(JsonValueKind.Null, message.GetProperty("content").ValueKind);
        var toolCall = message.GetProperty("tool_calls")[0];
        Assert.Equal("call_1", toolCall.GetProperty("id").GetString());
        Assert.Equal("lookup", toolCall.GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("""{"q":"x"}""", toolCall.GetProperty("function").GetProperty("arguments").GetString());
    }

    [Fact]
    public void MergeDelta_LastWinsForNonStringExtensions()
    {
        var assembler = new StreamingAssistantMessageAssembler();
        Merge(assembler, """{"foo":{"a":1}}""");
        Merge(assembler, """{"foo":{"a":2,"b":true}}""");

        using var document = JsonDocument.Parse(assembler.BuildMessageJson(SerializerOptions));
        var foo = document.RootElement.GetProperty("foo");
        Assert.Equal(2, foo.GetProperty("a").GetInt32());
        Assert.True(foo.GetProperty("b").GetBoolean());
    }

    private static void Merge(StreamingAssistantMessageAssembler assembler, string deltaJson)
    {
        using var document = JsonDocument.Parse(deltaJson);
        assembler.MergeDelta(document.RootElement);
    }
}
