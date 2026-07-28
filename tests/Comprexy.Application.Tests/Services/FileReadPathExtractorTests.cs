using Comprexy.Application.Services;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Tests.Services;

public class FileReadPathExtractorTests
{
    [Fact]
    public void TryExtract_PathTagInContent_ReturnsNormalizedPath()
    {
        var conversationId = Guid.NewGuid();
        var message = ConversationMessage.Create(
            conversationId,
            1,
            MessageRole.Tool,
            "<path>/workspace/repo/comprexy/comprexy.md</path>\n<type>file</type>",
            10,
            DateTimeOffset.UtcNow);

        var path = FileReadPathExtractor.TryExtract(message);

        Assert.Equal("/workspace/repo/comprexy/comprexy.md", path);
    }

    [Fact]
    public void TryExtract_FilePathInWireJson_ReturnsPath()
    {
        var conversationId = Guid.NewGuid();
        var message = ConversationMessage.Create(
            conversationId,
            1,
            MessageRole.Tool,
            "truncated",
            10,
            DateTimeOffset.UtcNow,
            """{"role":"tool","tool_call_id":"call_1","content":"","filePath":"src\\Foo.cs"}""");

        var path = FileReadPathExtractor.TryExtract(message);

        Assert.Equal("src/Foo.cs", path);
    }

    [Fact]
    public void TryExtract_PathTagInsideWireContent_ReturnsPath()
    {
        var conversationId = Guid.NewGuid();
        var message = ConversationMessage.Create(
            conversationId,
            1,
            MessageRole.Tool,
            "truncated",
            10,
            DateTimeOffset.UtcNow,
            """{"role":"tool","tool_call_id":"call_1","content":"<path>/tmp/a.md</path>\n<body>x</body>"}""");

        var path = FileReadPathExtractor.TryExtract(message);

        Assert.Equal("/tmp/a.md", path);
    }

    [Fact]
    public void TryExtract_OrdinaryUserMessage_ReturnsNull()
    {
        var conversationId = Guid.NewGuid();
        var message = ConversationMessage.Create(
            conversationId,
            1,
            MessageRole.User,
            "Please open /tmp/a.md later",
            10,
            DateTimeOffset.UtcNow);

        Assert.Null(FileReadPathExtractor.TryExtract(message));
    }

    [Fact]
    public void TryExtract_UserInjectedReadWithPathTag_ReturnsPath()
    {
        var conversationId = Guid.NewGuid();
        var message = ConversationMessage.Create(
            conversationId,
            1,
            MessageRole.User,
            "Called the Read tool",
            10,
            DateTimeOffset.UtcNow,
            """{"role":"user","content":[{"type":"text","text":"Called the Read tool with the following input: {\"filePath\":\"/workspace/repo/comprexy/comprexy.md\"}"},{"type":"text","text":"<path>/workspace/repo/comprexy/comprexy.md</path>\n<type>file</type>\n<content>\n1: hello\n</content>"}]}""");

        var path = FileReadPathExtractor.TryExtract(message);

        Assert.Equal("/workspace/repo/comprexy/comprexy.md", path);
    }

    [Fact]
    public void TryExtractToolCallId_FromWireJson()
    {
        var conversationId = Guid.NewGuid();
        var message = ConversationMessage.Create(
            conversationId,
            1,
            MessageRole.Tool,
            "x",
            1,
            DateTimeOffset.UtcNow,
            """{"role":"tool","tool_call_id":"call_abc","content":"ok"}""");

        Assert.Equal("call_abc", FileReadPathExtractor.TryExtractToolCallId(message));
    }

    [Fact]
    public void TryExtractToolCallId_TruncatedWireJson_RecoversId()
    {
        var conversationId = Guid.NewGuid();
        var truncated =
            """{"role":"tool","tool_call_id":"call_trunc","content":[{"type":"text","text":"unterminated""";
        var message = ConversationMessage.Create(
            conversationId,
            1,
            MessageRole.Tool,
            "x",
            1,
            DateTimeOffset.UtcNow,
            truncated);

        Assert.Equal("call_trunc", FileReadPathExtractor.TryExtractToolCallId(message));
    }
}
