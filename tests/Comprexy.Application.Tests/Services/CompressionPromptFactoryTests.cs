using Comprexy.Application.Services;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Tests.Services;

public class CompressionPromptFactoryTests
{
    private static readonly string Instruction = """
        You are updating working memory.
        Keep Files And Code Context.
        Summarize Markdown prose; preserve actual code when needed.
        """;

    private readonly CompressionPromptFactory _factory = new(Instruction);

    [Fact]
    public void BuildMessages_UsesRawWireJsonSoToolCallsAndResultsAreVisible()
    {
        var conversationId = Guid.NewGuid();
        var toolResult = ConversationMessage.Create(
            conversationId,
            0,
            MessageRole.Tool,
            "truncated",
            10,
            DateTimeOffset.UtcNow,
            """{"role":"tool","tool_call_id":"1","content":"public class Foo { }"}""");

        var messages = _factory.BuildMessages(null, [toolResult]);

        Assert.Equal(2, messages.Count);
        Assert.Contains("public class Foo", messages[1].Content);
        Assert.Contains("tool_call_id", messages[1].Content);
        Assert.Contains("Files And Code Context", messages[0].Content);
        Assert.Contains("file reads", messages[1].Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMessages_FallsBackToContentWhenRawWireMissing()
    {
        var conversationId = Guid.NewGuid();
        var message = ConversationMessage.Create(
            conversationId,
            0,
            MessageRole.User,
            "hello",
            1,
            DateTimeOffset.UtcNow);

        var messages = _factory.BuildMessages(null, [message]);

        Assert.Contains("User: hello", messages[1].Content);
    }

    [Fact]
    public void BuildMessagesFromFullRaw_OmitsPriorWorkingMemory()
    {
        var conversationId = Guid.NewGuid();
        var message = ConversationMessage.Create(
            conversationId,
            0,
            MessageRole.User,
            "hello",
            1,
            DateTimeOffset.UtcNow);

        var messages = _factory.BuildMessagesFromFullRaw([message]);

        Assert.Equal(2, messages.Count);
        Assert.Contains("Full Conversation Transcript", messages[1].Content);
        Assert.Contains("User: hello", messages[1].Content);
        Assert.DoesNotContain("## Existing Working Memory", messages[1].Content);
    }

    [Fact]
    public void Constructor_RejectsEmptyInstruction()
    {
        Assert.Throws<ArgumentException>(() => new CompressionPromptFactory("  "));
    }

    [Fact]
    public void BuildSmartInstructionMessage_AppendsRetainIndex()
    {
        var factory = new CompressionPromptFactory("fixed instruction", "smart instruction body");

        var message = factory.BuildSmartInstructionMessage("## Retain Index\nseq=1 user \"hi\"");

        Assert.Equal(MessageRole.User, message.Role);
        Assert.Contains("smart instruction body", message.Content);
        Assert.Contains("## Retain Index", message.Content);
        Assert.Contains("seq=1", message.Content);
    }

    [Fact]
    public void BuildMessagesFromFullRaw_UsesFixedInstruction()
    {
        var factory = new CompressionPromptFactory("fixed instruction", "smart instruction");
        var conversationId = Guid.NewGuid();
        var message = ConversationMessage.Create(
            conversationId,
            42,
            MessageRole.User,
            "hello",
            1,
            DateTimeOffset.UtcNow);

        var messages = factory.BuildMessagesFromFullRaw([message]);

        Assert.Equal("fixed instruction", messages[0].Content);
        Assert.Contains("Full Conversation Transcript", messages[1].Content);
        Assert.DoesNotContain("sequence=42", messages[1].Content);
    }

    [Fact]
    public void BuildMessages_StripsReasoningContentFromWireJson()
    {
        var factory = new CompressionPromptFactory("fixed", stripReasoningContent: true);
        var conversationId = Guid.NewGuid();
        var message = ConversationMessage.Create(
            conversationId,
            0,
            MessageRole.Assistant,
            "visible",
            5,
            DateTimeOffset.UtcNow,
            """{"role":"assistant","content":"visible","reasoning_content":"hidden thoughts","tool_calls":[]}""");

        var messages = factory.BuildMessages(null, [message]);

        Assert.DoesNotContain("reasoning_content", messages[1].Content);
        Assert.DoesNotContain("hidden thoughts", messages[1].Content);
        Assert.Contains("visible", messages[1].Content);
    }

    [Fact]
    public void BuildMessages_SimplifiesToolCallsForCompression()
    {
        var factory = new CompressionPromptFactory("fixed", stripReasoningContent: true);
        var conversationId = Guid.NewGuid();
        var message = ConversationMessage.Create(
            conversationId,
            0,
            MessageRole.Assistant,
            string.Empty,
            5,
            DateTimeOffset.UtcNow,
            """{"role":"assistant","content":null,"tool_calls":[{"id":"call_1","type":"function","function":{"name":"Read","arguments":"{\"path\":\"a.md\"}"}}]}""");

        var messages = factory.BuildMessages(null, [message]);
        var userContent = messages[1].Content;

        Assert.DoesNotContain("\"type\":\"function\"", userContent);
        Assert.DoesNotContain("\"function\":", userContent);
        Assert.Contains("\"id\":\"call_1\"", userContent);
        Assert.Contains("\"name\":\"Read\"", userContent);
        Assert.Contains("\"path\":\"a.md\"", userContent);
    }

    [Fact]
    public void BuildMessages_KeepsNonFunctionToolCallTypes_WhenFlattening()
    {
        var factory = new CompressionPromptFactory("fixed");
        var conversationId = Guid.NewGuid();
        var message = ConversationMessage.Create(
            conversationId,
            0,
            MessageRole.Assistant,
            string.Empty,
            5,
            DateTimeOffset.UtcNow,
            """{"role":"assistant","tool_calls":[{"id":"call_1","type":"custom","function":{"name":"x","arguments":"{}"}}]}""");

        var messages = factory.BuildMessages(null, [message]);
        var userContent = messages[1].Content;

        Assert.Contains("\"type\":\"custom\"", userContent);
        Assert.DoesNotContain("\"function\":", userContent);
        Assert.Contains("\"name\":\"x\"", userContent);
    }

    [Fact]
    public void BuildMessages_DoesNotStripMultimodalContentTypes()
    {
        var factory = new CompressionPromptFactory("fixed");
        var conversationId = Guid.NewGuid();
        var message = ConversationMessage.Create(
            conversationId,
            0,
            MessageRole.User,
            "hello",
            5,
            DateTimeOffset.UtcNow,
            """{"role":"user","content":[{"type":"text","text":"hello"}]}""");

        var messages = factory.BuildMessages(null, [message]);

        Assert.Contains("\"type\":\"text\"", messages[1].Content);
        Assert.Contains("\"text\":\"hello\"", messages[1].Content);
    }

    [Fact]
    public void BuildMessages_WhenStripDisabled_KeepsReasoningContent()
    {
        var factory = new CompressionPromptFactory("fixed", stripReasoningContent: false);
        var conversationId = Guid.NewGuid();
        var message = ConversationMessage.Create(
            conversationId,
            0,
            MessageRole.Assistant,
            "visible",
            5,
            DateTimeOffset.UtcNow,
            """{"role":"assistant","content":"visible","reasoning_content":"hidden thoughts"}""");

        var messages = factory.BuildMessages(null, [message]);

        Assert.Contains("reasoning_content", messages[1].Content);
        Assert.Contains("hidden thoughts", messages[1].Content);
    }
}
