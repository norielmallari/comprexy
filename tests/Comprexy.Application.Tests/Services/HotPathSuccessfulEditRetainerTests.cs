using System.Text.Json;
using Comprexy.Application.Services;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Tests.Services;

public class HotPathSuccessfulEditRetainerTests
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
        string toolCallId) =>
        ConversationMessage.Create(
            conversationId,
            sequence,
            MessageRole.Tool,
            "Error: The string to replace was not found in the file (even after relaxing whitespace).",
            10,
            DateTimeOffset.UtcNow,
            $"{{\"role\":\"tool\",\"tool_call_id\":\"{toolCallId}\",\"content\":\"Error: The string to replace was not found in the file (even after relaxing whitespace).\"}}");

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

    private static ConversationMessage User(Guid conversationId, int sequence) =>
        ConversationMessage.Create(conversationId, sequence, MessageRole.User, $"u-{sequence}", 3, DateTimeOffset.UtcNow);

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
    public void SelectPinnedMessages_FailureAfterSuccess_PinsSuccessfulGroup()
    {
        var conversationId = Guid.NewGuid();
        const string path = "/workspace/repo/top-bar.test.tsx";
        var successAssistant = AssistantStrReplace(conversationId, 1, "ok1", path, "old-a");
        var successTool = ToolSuccess(conversationId, 2, "ok1", path);
        var failAssistant = AssistantStrReplace(conversationId, 3, "bad1", path, "old-a");
        var failTool = ToolFail(conversationId, 4, "bad1");
        var tipUser = User(conversationId, 5);
        var universe = new[] { successAssistant, successTool, failAssistant, failTool, tipUser };

        var pinned = HotPathSuccessfulEditRetainer.SelectPinnedMessages(universe);

        Assert.Equal([1, 2], pinned.Select(m => m.Sequence).ToArray());
        Assert.Contains(successAssistant.Id, pinned.Select(m => m.Id));
        Assert.Contains(successTool.Id, pinned.Select(m => m.Id));
    }

    [Fact]
    public void SelectPinnedMessages_NoFailures_ReturnsEmpty()
    {
        var conversationId = Guid.NewGuid();
        const string path = "/workspace/repo/a.ts";
        var universe = new[]
        {
            AssistantStrReplace(conversationId, 1, "ok1", path, "old"),
            ToolSuccess(conversationId, 2, "ok1", path),
            User(conversationId, 3)
        };

        Assert.Empty(HotPathSuccessfulEditRetainer.SelectPinnedMessages(universe));
    }

    [Fact]
    public void SelectPinnedMessages_FailureWithoutPriorSuccess_ReturnsEmpty()
    {
        var conversationId = Guid.NewGuid();
        const string path = "/workspace/repo/a.ts";
        var universe = new[]
        {
            AssistantStrReplace(conversationId, 1, "bad1", path, "ghost"),
            ToolFail(conversationId, 2, "bad1"),
            User(conversationId, 3)
        };

        Assert.Empty(HotPathSuccessfulEditRetainer.SelectPinnedMessages(universe));
    }

    [Fact]
    public void SelectPinnedMessages_KeepsLastSuccessWhenMultiple()
    {
        var conversationId = Guid.NewGuid();
        const string path = "/workspace/repo/a.ts";
        var universe = new[]
        {
            AssistantStrReplace(conversationId, 1, "ok1", path, "a"),
            ToolSuccess(conversationId, 2, "ok1", path),
            AssistantStrReplace(conversationId, 3, "ok2", path, "b"),
            ToolSuccess(conversationId, 4, "ok2", path),
            AssistantStrReplace(conversationId, 5, "bad1", path, "ghost"),
            ToolFail(conversationId, 6, "bad1"),
        };

        var pinned = HotPathSuccessfulEditRetainer.SelectPinnedMessages(universe);

        Assert.Equal([3, 4], pinned.Select(m => m.Sequence).ToArray());
    }
}
