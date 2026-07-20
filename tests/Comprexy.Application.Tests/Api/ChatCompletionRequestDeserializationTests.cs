using System.Text.Json;
using Comprexy.Api.Contracts;

namespace Comprexy.Application.Tests.Api;

public class ChatCompletionRequestDeserializationTests
{
    [Fact]
    public void Deserializes_StringContent()
    {
        const string json = """
            {
              "messages": [
                { "role": "user", "content": "hello" }
              ]
            }
            """;

        var request = JsonSerializer.Deserialize<ChatCompletionRequestDto>(json);

        Assert.NotNull(request);
        Assert.Single(request!.Messages);
        Assert.Equal("hello", request.Messages[0].Content);
    }

    [Fact]
    public void Deserializes_MultimodalArrayContent_AsFlattenedText()
    {
        const string json = """
            {
              "messages": [
                { "role": "system", "content": "You are helpful." },
                {
                  "role": "user",
                  "content": [
                    { "type": "text", "text": "Look at this" },
                    { "type": "image_url", "image_url": { "url": "https://example.com/a.png" } },
                    { "type": "text", "text": "and continue" }
                  ]
                }
              ]
            }
            """;

        var request = JsonSerializer.Deserialize<ChatCompletionRequestDto>(json);

        Assert.NotNull(request);
        Assert.Equal(2, request!.Messages.Count);
        Assert.Equal("You are helpful.", request.Messages[0].Content);
        Assert.Equal(
            "Look at this\n[image: https://example.com/a.png]\nand continue",
            request.Messages[1].Content);
    }

    [Fact]
    public void Deserializes_NullContent_AsEmptyString()
    {
        const string json = """
            {
              "messages": [
                { "role": "assistant", "content": null }
              ]
            }
            """;

        var request = JsonSerializer.Deserialize<ChatCompletionRequestDto>(json);

        Assert.NotNull(request);
        Assert.Equal(string.Empty, request!.Messages[0].Content);
    }

    [Fact]
    public void Deserializes_Stop_AsStringOrArray()
    {
        const string stringStop = """{ "messages": [], "stop": "END" }""";
        const string arrayStop = """{ "messages": [], "stop": ["END", "STOP"] }""";

        var fromString = JsonSerializer.Deserialize<ChatCompletionRequestDto>(stringStop);
        var fromArray = JsonSerializer.Deserialize<ChatCompletionRequestDto>(arrayStop);

        Assert.Equal(["END"], fromString!.Stop);
        Assert.Equal(["END", "STOP"], fromArray!.Stop);
    }
}
