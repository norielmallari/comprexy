using System.Text.Json;
using Comprexy.Api.Mapping;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Tests.Api;

public class ChatCompletionRequestParserTests
{
    [Fact]
    public void Parse_PreservesRawRequestFieldsAndMessageWireObjects()
    {
        const string json = """
            {
              "model": "client-model",
              "temperature": 0.7,
              "tools": [{"type":"function","function":{"name":"lookup"}}],
              "messages": [
                { "role": "system", "content": "You are helpful." },
                {
                  "role": "user",
                  "content": [
                    { "type": "text", "text": "Look at this" },
                    { "type": "image_url", "image_url": { "url": "https://example.com/a.png" } }
                  ],
                  "name": "eli"
                }
              ]
            }
            """;

        using var document = JsonDocument.Parse(json);
        var request = ChatCompletionRequestParser.Parse(document.RootElement.Clone(), "conv-1");

        Assert.Equal(2, request.Messages.Count);
        Assert.Equal(MessageRole.User, request.Messages[1].Role);
        Assert.Equal("Look at this\n[image: https://example.com/a.png]", request.Messages[1].Content);
        Assert.True(request.RawRequest.TryGetProperty("tools", out _));
        Assert.Equal(0.7, request.CallOptions.Temperature);

        Assert.NotNull(request.Messages[1].RawWireMessage);
        var rawMessage = request.Messages[1].RawWireMessage!.Value;
        Assert.True(rawMessage.TryGetProperty("name", out var name));
        Assert.Equal("eli", name.GetString());
        Assert.Equal(JsonValueKind.Array, rawMessage.GetProperty("content").ValueKind);
    }
}
