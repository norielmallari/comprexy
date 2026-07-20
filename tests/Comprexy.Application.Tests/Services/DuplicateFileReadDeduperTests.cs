using Comprexy.Application.Services;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Tests.Services;

public class DuplicateFileReadDeduperTests
{
    private static ConversationMessage ToolRead(
        Guid conversationId,
        int sequence,
        string path,
        string toolCallId = "call_x") =>
        ConversationMessage.Create(
            conversationId,
            sequence,
            MessageRole.Tool,
            $"<path>{path}</path>",
            10,
            DateTimeOffset.UtcNow,
            $"{{\"role\":\"tool\",\"tool_call_id\":\"{toolCallId}\",\"content\":\"<path>{path}</path>\"}}");

    private static ConversationMessage AssistantSingleCall(
        Guid conversationId,
        int sequence,
        string toolCallId,
        string path) =>
        ConversationMessage.Create(
            conversationId,
            sequence,
            MessageRole.Assistant,
            string.Empty,
            5,
            DateTimeOffset.UtcNow,
            $"{{\"role\":\"assistant\",\"tool_calls\":[{{\"id\":\"{toolCallId}\",\"type\":\"function\",\"function\":{{\"name\":\"Read\",\"arguments\":\"{{\\\"filePath\\\":\\\"{path}\\\"}}\"}}}}]}}");

    private static ConversationMessage User(Guid conversationId, int sequence) =>
        ConversationMessage.Create(conversationId, sequence, MessageRole.User, $"u-{sequence}", 3, DateTimeOffset.UtcNow);

    [Fact]
    public void Apply_ThreeReadsSamePath_KeepsNewestOnly()
    {
        var conversationId = Guid.NewGuid();
        const string path = "/proj/a.md";
        var retain = new[]
        {
            ToolRead(conversationId, 1, path, "c1"),
            ToolRead(conversationId, 2, path, "c2"),
            ToolRead(conversationId, 3, path, "c3"),
            User(conversationId, 4)
        };

        var result = DuplicateFileReadDeduper.Apply(retain, retain, forcedTipSequence: 4);

        Assert.Equal([3, 4], result.Retain.Select(m => m.Sequence).ToArray());
        Assert.Equal([1, 2], result.DroppedSequences.ToArray());
    }

    [Fact]
    public void Apply_DifferentPaths_Unaffected()
    {
        var conversationId = Guid.NewGuid();
        var retain = new[]
        {
            ToolRead(conversationId, 1, "/a.md", "c1"),
            ToolRead(conversationId, 2, "/b.md", "c2"),
            User(conversationId, 3)
        };

        var result = DuplicateFileReadDeduper.Apply(retain, retain, forcedTipSequence: 3);

        Assert.Equal([1, 2, 3], result.Retain.Select(m => m.Sequence).ToArray());
        Assert.Empty(result.DroppedSequences);
    }

    [Fact]
    public void Apply_NeverDropsForcedTip()
    {
        var conversationId = Guid.NewGuid();
        const string path = "/proj/a.md";
        var tip = ToolRead(conversationId, 5, path, "c5");
        var retain = new[]
        {
            ToolRead(conversationId, 1, path, "c1"),
            ToolRead(conversationId, 3, path, "c3"),
            tip
        };

        var result = DuplicateFileReadDeduper.Apply(retain, retain, forcedTipSequence: 5);

        Assert.Equal([5], result.Retain.Select(m => m.Sequence).ToArray());
        Assert.Equal([1, 3], result.DroppedSequences.ToArray());
        Assert.DoesNotContain(5, result.DroppedSequences);
    }

    [Fact]
    public void Apply_NoExtractablePath_NoOp()
    {
        var conversationId = Guid.NewGuid();
        var retain = new[]
        {
            ConversationMessage.Create(conversationId, 1, MessageRole.Tool, "no path here", 5, DateTimeOffset.UtcNow),
            ConversationMessage.Create(conversationId, 2, MessageRole.Tool, "still none", 5, DateTimeOffset.UtcNow),
            User(conversationId, 3)
        };

        var result = DuplicateFileReadDeduper.Apply(retain, retain, forcedTipSequence: 3);

        Assert.Equal(3, result.Retain.Count);
        Assert.Empty(result.DroppedSequences);
    }

    [Fact]
    public void Apply_DropsSingleCallAssistantParentWithOlderRead()
    {
        var conversationId = Guid.NewGuid();
        const string path = "/proj/a.md";
        var slice = new[]
        {
            AssistantSingleCall(conversationId, 1, "c1", path),
            ToolRead(conversationId, 2, path, "c1"),
            AssistantSingleCall(conversationId, 3, "c2", path),
            ToolRead(conversationId, 4, path, "c2"),
            User(conversationId, 5)
        };

        var result = DuplicateFileReadDeduper.Apply(slice, slice, forcedTipSequence: 5);

        Assert.Equal([3, 4, 5], result.Retain.Select(m => m.Sequence).ToArray());
        Assert.Equal([1, 2], result.DroppedSequences.ToArray());
    }

    [Fact]
    public void Apply_MultiToolCallAssistant_NotDroppedWholesale()
    {
        var conversationId = Guid.NewGuid();
        const string path = "/proj/a.md";
        var multiAssistant = ConversationMessage.Create(
            conversationId,
            1,
            MessageRole.Assistant,
            string.Empty,
            5,
            DateTimeOffset.UtcNow,
            """{"role":"assistant","tool_calls":[{"id":"c1","type":"function","function":{"name":"Read","arguments":"{\"filePath\":\"/proj/a.md\"}"}},{"id":"c2","type":"function","function":{"name":"Read","arguments":"{\"filePath\":\"/proj/b.md\"}"}}]}""");
        var olderA = ToolRead(conversationId, 2, path, "c1");
        var b = ToolRead(conversationId, 3, "/proj/b.md", "c2");
        var newerA = ToolRead(conversationId, 4, path, "c3");
        var tip = User(conversationId, 5);
        var retain = new[] { multiAssistant, olderA, b, newerA, tip };

        var result = DuplicateFileReadDeduper.Apply(retain, retain, forcedTipSequence: 5);

        Assert.Contains(result.Retain, m => m.Sequence == 1);
        Assert.DoesNotContain(result.Retain, m => m.Sequence == 2);
        Assert.Contains(result.Retain, m => m.Sequence == 3);
        Assert.Contains(result.Retain, m => m.Sequence == 4);
    }

    [Fact]
    public void Apply_MultiToolCallAssistant_DroppedWhenAllResultsAreDuplicates()
    {
        var conversationId = Guid.NewGuid();
        var olderAssistant = ConversationMessage.Create(
            conversationId,
            1,
            MessageRole.Assistant,
            string.Empty,
            5,
            DateTimeOffset.UtcNow,
            """{"role":"assistant","tool_calls":[{"id":"c1","type":"function","function":{"name":"Read","arguments":"{\"filePath\":\"/personas/tina.md\"}"}},{"id":"c2","type":"function","function":{"name":"Read","arguments":"{\"filePath\":\"/personas/enhanced.md\"}"}},{"id":"c3","type":"function","function":{"name":"Read","arguments":"{\"filePath\":\"/personas/chat.md\"}"}}]}""");
        var newerAssistant = ConversationMessage.Create(
            conversationId,
            10,
            MessageRole.Assistant,
            string.Empty,
            5,
            DateTimeOffset.UtcNow,
            """{"role":"assistant","tool_calls":[{"id":"n1","type":"function","function":{"name":"Read","arguments":"{\"filePath\":\"/personas/tina.md\"}"}},{"id":"n2","type":"function","function":{"name":"Read","arguments":"{\"filePath\":\"/personas/enhanced.md\"}"}},{"id":"n3","type":"function","function":{"name":"Read","arguments":"{\"filePath\":\"/personas/chat.md\"}"}}]}""");

        var messages = new[]
        {
            olderAssistant,
            ToolRead(conversationId, 2, "/personas/tina.md", "c1"),
            ToolRead(conversationId, 3, "/personas/enhanced.md", "c2"),
            ToolRead(conversationId, 4, "/personas/chat.md", "c3"),
            newerAssistant,
            ToolRead(conversationId, 11, "/personas/tina.md", "n1"),
            ToolRead(conversationId, 12, "/personas/enhanced.md", "n2"),
            ToolRead(conversationId, 13, "/personas/chat.md", "n3"),
            User(conversationId, 14)
        };

        var result = DuplicateFileReadDeduper.Apply(messages, messages, forcedTipSequence: 14);

        Assert.DoesNotContain(result.Retain, m => m.Sequence is 1 or 2 or 3 or 4);
        Assert.Equal([10, 11, 12, 13, 14], result.Retain.Select(m => m.Sequence).ToArray());
    }

    [Fact]
    public void Apply_UserInjectedFileReads_SamePath_KeepsNewestOnly()
    {
        var conversationId = Guid.NewGuid();
        const string path = "/Users/eli/Projects/kilo-code/comprexy/comprexy.md";

        ConversationMessage InjectedRead(int sequence) =>
            ConversationMessage.Create(
                conversationId,
                sequence,
                MessageRole.User,
                "Called the Read tool",
                100,
                DateTimeOffset.UtcNow,
                $"{{\"role\":\"user\",\"content\":[{{\"type\":\"text\",\"text\":\"Called the Read tool with the following input: {{\\\"filePath\\\":\\\"{path}\\\"}}\"}},{{\"type\":\"text\",\"text\":\"<path>{path}</path>\\n<body>dump-{sequence}</body>\"}}]}}");

        var messages = new[]
        {
            InjectedRead(6),
            InjectedRead(8),
            InjectedRead(10),
            User(conversationId, 11)
        };

        var result = DuplicateFileReadDeduper.Apply(messages, messages, forcedTipSequence: 11);

        Assert.Equal([10, 11], result.Retain.Select(m => m.Sequence).ToArray());
        Assert.Equal([6, 8], result.DroppedSequences.ToArray());
    }
}
