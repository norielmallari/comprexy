using Comprexy.Application.Services;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Tests.Services;

public class ToolCallWireHelperTests
{
    [Fact]
    public void TryExtractToolCallId_FromWireJson()
    {
        var message = ConversationMessage.Create(
            Guid.NewGuid(),
            1,
            MessageRole.Tool,
            "x",
            1,
            DateTimeOffset.UtcNow,
            """{"role":"tool","tool_call_id":"call_abc","content":"ok"}""");

        Assert.Equal("call_abc", ToolCallWireHelper.TryExtractToolCallId(message));
    }

    [Fact]
    public void TryExtractToolCallId_TruncatedWireJson_RecoversId()
    {
        var truncated =
            """{"role":"tool","tool_call_id":"call_trunc","content":[{"type":"text","text":"unterminated""";
        var message = ConversationMessage.Create(
            Guid.NewGuid(),
            1,
            MessageRole.Tool,
            "x",
            1,
            DateTimeOffset.UtcNow,
            truncated);

        Assert.Equal("call_trunc", ToolCallWireHelper.TryExtractToolCallId(message));
    }

    [Fact]
    public void TryExtractToolCallId_NonToolRole_ReturnsNull()
    {
        var message = ConversationMessage.Create(
            Guid.NewGuid(),
            1,
            MessageRole.Assistant,
            "x",
            1,
            DateTimeOffset.UtcNow,
            """{"role":"assistant","tool_call_id":"call_abc"}""");

        Assert.Null(ToolCallWireHelper.TryExtractToolCallId(message));
    }

    [Fact]
    public void GetAssistantToolCallIds_ReturnsEveryAnnouncedId()
    {
        var message = ConversationMessage.Create(
            Guid.NewGuid(),
            1,
            MessageRole.Assistant,
            string.Empty,
            1,
            DateTimeOffset.UtcNow,
            """
            {"role":"assistant","tool_calls":[
              {"id":"call_1","type":"function","function":{"name":"read","arguments":"{}"}},
              {"id":"call_2","type":"function","function":{"name":"write","arguments":"{}"}}
            ]}
            """);

        Assert.Equal(["call_1", "call_2"], ToolCallWireHelper.GetAssistantToolCallIds(message));
    }

    [Fact]
    public void GetAssistantToolCallIds_IncludesCallsWithoutFunctionName()
    {
        // Chain bookkeeping must see partially streamed / name-less calls that
        // ParseAssistantToolCalls intentionally skips, or the chain looks closed too early.
        var message = ConversationMessage.Create(
            Guid.NewGuid(),
            1,
            MessageRole.Assistant,
            string.Empty,
            1,
            DateTimeOffset.UtcNow,
            """{"role":"assistant","tool_calls":[{"id":"call_nameless","type":"function","function":{"arguments":"{}"}}]}""");

        Assert.Equal(["call_nameless"], ToolCallWireHelper.GetAssistantToolCallIds(message));
        Assert.Empty(ToolCallWireHelper.ParseAssistantToolCalls(message.RawWireJson));
    }

    [Fact]
    public void GetAssistantToolCallIds_UnparseableWire_ReturnsEmpty()
    {
        var message = ConversationMessage.Create(
            Guid.NewGuid(),
            1,
            MessageRole.Assistant,
            string.Empty,
            1,
            DateTimeOffset.UtcNow,
            """{"role":"assistant","tool_calls":[{"id":"call_open""");

        Assert.Empty(ToolCallWireHelper.GetAssistantToolCallIds(message));
    }
}
