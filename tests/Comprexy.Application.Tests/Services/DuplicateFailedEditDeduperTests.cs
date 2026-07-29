using System.Text.Json;
using Comprexy.Application.Services;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Tests.Services;

public class DuplicateFailedEditDeduperTests
{
    private static ConversationMessage AssistantStrReplace(
        Guid conversationId,
        int sequence,
        string toolCallId,
        string path,
        string oldString) =>
        ConversationMessage.Create(
            conversationId,
            sequence,
            MessageRole.Assistant,
            string.Empty,
            5,
            DateTimeOffset.UtcNow,
            BuildAssistantWire(toolCallId, path, oldString));

    private static ConversationMessage ToolFail(
        Guid conversationId,
        int sequence,
        string toolCallId,
        string content =
            "Error: The string to replace was not found in the file (even after relaxing whitespace).") =>
        ConversationMessage.Create(
            conversationId,
            sequence,
            MessageRole.Tool,
            content,
            10,
            DateTimeOffset.UtcNow,
            $"{{\"role\":\"tool\",\"tool_call_id\":\"{toolCallId}\",\"content\":{JsonSerializer.Serialize(content)}}}");

    private static ConversationMessage ToolSuccess(
        Guid conversationId,
        int sequence,
        string toolCallId,
        string path) =>
        ConversationMessage.Create(
            conversationId,
            sequence,
            MessageRole.Tool,
            $"The file {path} has been updated.",
            8,
            DateTimeOffset.UtcNow,
            $"{{\"role\":\"tool\",\"tool_call_id\":\"{toolCallId}\",\"content\":\"The file {path} has been updated.\"}}");

    private static string BuildAssistantWire(string toolCallId, string path, string oldString)
    {
        var args = JsonSerializer.Serialize(new
        {
            path,
            old_string = oldString,
            new_string = "replacement"
        });
        var argsEscaped = JsonSerializer.Serialize(args);
        return
            $"{{\"role\":\"assistant\",\"tool_calls\":[{{\"id\":\"{toolCallId}\",\"type\":\"function\",\"function\":{{\"name\":\"StrReplace\",\"arguments\":{argsEscaped}}}}}]}}";
    }

    [Fact]
    public void Apply_ThreeIdenticalFailures_KeepsNewestOnly()
    {
        var conversationId = Guid.NewGuid();
        const string path = "/workspace/repo/a.ts";
        const string old = "const x = 1;";
        var messages = new[]
        {
            AssistantStrReplace(conversationId, 1, "c1", path, old),
            ToolFail(conversationId, 2, "c1"),
            AssistantStrReplace(conversationId, 3, "c2", path, old),
            ToolFail(conversationId, 4, "c2"),
            AssistantStrReplace(conversationId, 5, "c3", path, old),
            ToolFail(conversationId, 6, "c3"),
        };

        var result = DuplicateFailedEditDeduper.Apply(messages, forcedTipSequence: null);

        Assert.Equal([5, 6], result.Retain.Select(m => m.Sequence).ToArray());
        Assert.Equal([1, 2, 3, 4], result.DroppedSequences.ToArray());
        Assert.True(result.DroppedAny);
    }

    [Fact]
    public void Apply_DifferentOldStrings_Unaffected()
    {
        var conversationId = Guid.NewGuid();
        const string path = "/workspace/repo/a.ts";
        var messages = new[]
        {
            AssistantStrReplace(conversationId, 1, "c1", path, "old-a"),
            ToolFail(conversationId, 2, "c1"),
            AssistantStrReplace(conversationId, 3, "c2", path, "old-b"),
            ToolFail(conversationId, 4, "c2"),
        };

        var result = DuplicateFailedEditDeduper.Apply(messages, forcedTipSequence: null);

        Assert.Equal([1, 2, 3, 4], result.Retain.Select(m => m.Sequence).ToArray());
        Assert.Empty(result.DroppedSequences);
    }

    [Fact]
    public void Apply_SuccessNotDedupedAsFailure()
    {
        var conversationId = Guid.NewGuid();
        const string path = "/workspace/repo/a.ts";
        var messages = new[]
        {
            AssistantStrReplace(conversationId, 1, "c1", path, "old"),
            ToolSuccess(conversationId, 2, "c1", path),
            AssistantStrReplace(conversationId, 3, "c2", path, "old"),
            ToolSuccess(conversationId, 4, "c2", path),
        };

        var result = DuplicateFailedEditDeduper.Apply(messages, forcedTipSequence: null);

        Assert.Equal(4, result.Retain.Count);
        Assert.Empty(result.DroppedSequences);
    }

    [Fact]
    public void Apply_NeverDropsForcedTip()
    {
        var conversationId = Guid.NewGuid();
        const string path = "/workspace/repo/a.ts";
        const string old = "const x = 1;";
        var tip = ToolFail(conversationId, 6, "c3");
        var messages = new[]
        {
            AssistantStrReplace(conversationId, 1, "c1", path, old),
            ToolFail(conversationId, 2, "c1"),
            AssistantStrReplace(conversationId, 3, "c2", path, old),
            ToolFail(conversationId, 4, "c2"),
            AssistantStrReplace(conversationId, 5, "c3", path, old),
            tip
        };

        var result = DuplicateFailedEditDeduper.Apply(messages, forcedTipSequence: 6);

        Assert.Contains(6, result.Retain.Select(m => m.Sequence));
        Assert.DoesNotContain(6, result.DroppedSequences);
        Assert.Equal([5, 6], result.Retain.Select(m => m.Sequence).ToArray());
    }
}
