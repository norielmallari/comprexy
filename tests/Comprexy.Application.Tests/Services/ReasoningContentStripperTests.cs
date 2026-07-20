using System.Text.Json.Nodes;
using Comprexy.Application.Services;

namespace Comprexy.Application.Tests.Services;

public class ReasoningContentStripperTests
{
    [Fact]
    public void StripFromWireJson_RemovesReasoningContent()
    {
        var input = """{"role":"assistant","content":"hi","reasoning_content":"think hard"}""";

        var result = ReasoningContentStripper.StripFromWireJson(input, enabled: true);

        Assert.DoesNotContain("reasoning_content", result);
        Assert.Contains("\"content\":\"hi\"", result);
        Assert.Contains("\"role\":\"assistant\"", result);
    }

    [Fact]
    public void StripFromWireJson_RemovesReasoningAlias()
    {
        var input = """{"role":"assistant","content":"hi","reasoning":"secret"}""";

        var result = ReasoningContentStripper.StripFromWireJson(input, enabled: true);

        Assert.DoesNotContain("reasoning", result);
        Assert.Contains("\"content\":\"hi\"", result);
    }

    [Fact]
    public void StripFromWireJson_WhenDisabled_LeavesFields()
    {
        var input = """{"role":"assistant","content":"hi","reasoning_content":"think"}""";

        var result = ReasoningContentStripper.StripFromWireJson(input, enabled: false);

        Assert.Contains("reasoning_content", result);
    }

    [Fact]
    public void StripFromMessagesArray_StripsEachMessage()
    {
        var messages = JsonNode.Parse("""
            [
              {"role":"assistant","content":"a","reasoning_content":"x"},
              {"role":"user","content":"b"}
            ]
            """)!;

        ReasoningContentStripper.StripFromMessagesArray(messages, enabled: true);

        Assert.DoesNotContain("reasoning_content", messages.ToJsonString());
        Assert.Contains("\"content\":\"a\"", messages.ToJsonString());
    }
}
