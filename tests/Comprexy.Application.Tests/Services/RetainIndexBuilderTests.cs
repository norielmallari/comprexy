using Comprexy.Application.Services;
using Comprexy.Domain.Entities;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Tests.Services;

public class RetainIndexBuilderTests
{
    [Fact]
    public void Build_IncludesSequencesAndOmitsToolBodies()
    {
        var conversationId = Guid.NewGuid();
        var assistant = ConversationMessage.Create(
            conversationId,
            0,
            MessageRole.Assistant,
            string.Empty,
            5,
            DateTimeOffset.UtcNow,
            """{"role":"assistant","tool_calls":[{"id":"c1","type":"function","function":{"name":"Read","arguments":"{\"filePath\":\"/proj/a.md\"}"}}]}""");
        var tool = ConversationMessage.Create(
            conversationId,
            1,
            MessageRole.Tool,
            "line1\nline2\nSECRET_BODY",
            20,
            DateTimeOffset.UtcNow,
            """{"role":"tool","tool_call_id":"c1","content":"<path>/proj/a.md</path>\nSECRET_BODY"}""");
        var user = ConversationMessage.Create(
            conversationId,
            2,
            MessageRole.User,
            "Please keep the public API stable for Foo",
            10,
            DateTimeOffset.UtcNow);

        var index = RetainIndexBuilder.Build([assistant, tool, user], tipSequence: 2);

        Assert.Contains("seq=0", index);
        Assert.Contains("tool_calls=Read", index);
        Assert.Contains("seq=1", index);
        Assert.Contains("path=/proj/a.md", index);
        Assert.Contains("[body omitted]", index);
        Assert.DoesNotContain("SECRET_BODY", index);
        Assert.Contains("seq=2", index);
        Assert.Contains("(tip)", index);
        Assert.Contains("public API", index);
    }
}
