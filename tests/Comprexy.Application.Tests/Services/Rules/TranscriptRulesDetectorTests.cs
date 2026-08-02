using Comprexy.Application.Models;
using Comprexy.Application.Services.Rules;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Tests.Services.Rules;

public class TranscriptRulesDetectorTests
{
    private readonly TranscriptRulesDetector _detector = new();

    [Fact]
    public void Detect_UserRulesXml_ExtractsKeyedBlock()
    {
        var messages = new List<ChatMessage>
        {
            new(MessageRole.User, """
                <rules>
                <rule name="rule-a">Transcript rule body.</rule>
                </rules>
                """)
        };

        var rules = _detector.Detect(messages);

        Assert.Single(rules);
        Assert.Equal("rule-a", rules[0].NormalizedKey);
        Assert.Equal("Transcript rule body.", rules[0].Body);
        Assert.Equal(RuleSource.Transcript, rules[0].Source);
    }

    [Fact]
    public void Detect_ReadAppendixToolResult_ExtractsRuleBlock()
    {
        var messages = new List<ChatMessage>
        {
            new(MessageRole.Tool, """
                The following cursor rule files are relevant to this file:
                --- rule: /workspace/repo/.cursor/rules/read-append.mdc ---
                Read-append rule body.
                """)
        };

        var rules = _detector.Detect(messages);

        Assert.Single(rules);
        Assert.Equal("read-append.mdc", rules[0].NormalizedKey);
        Assert.Equal("Read-append rule body.", rules[0].Body);
    }

    [Fact]
    public void Detect_AssistantOnly_Ignored()
    {
        var messages = new List<ChatMessage>
        {
            new(MessageRole.Assistant, """
                <rules><rule name="ignored">nope</rule></rules>
                """)
        };

        Assert.Empty(_detector.Detect(messages));
    }
}
