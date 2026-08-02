using Comprexy.Application.Services.Rules;

namespace Comprexy.Application.Tests.Services.Rules;

public class WorkingMemoryRulesSectionTests
{
    [Fact]
    public void FormatAndParse_RoundTripsKeysAndBodies()
    {
        var rules = new[]
        {
            new RuleBlock("rule-a.md", "rule-a.md", "Body A", RuleSource.System),
            new RuleBlock("rule-b.md", "rule-b.md", "Body B", RuleSource.System)
        };

        var formatted = WorkingMemoryRulesSection.FormatSection(rules);
        var parsed = WorkingMemoryRulesSection.TryParse(formatted);

        Assert.Equal(2, parsed.Count);
        Assert.Equal("Body A", parsed["rule-a.md"].Body);
        Assert.Equal("Body B", parsed["rule-b.md"].Body);
    }

    [Fact]
    public void ReplaceRulesSection_PreservesOtherSections()
    {
        var wm = """
            # Working Memory

            ## Current Goal
            Keep goal.

            ## Rules
            - None yet.

            ## Files And Code Context
            - path
            """;

        var replaced = WorkingMemoryRulesSection.ReplaceRulesSection(
            wm,
            WorkingMemoryRulesSection.FormatSection([
                new RuleBlock("new.md", "new.md", "New body", RuleSource.System)
            ]));

        Assert.Contains("Keep goal.", replaced);
        Assert.Contains("### rule:new.md", replaced);
        Assert.Contains("New body", replaced);
        Assert.Contains("## Files And Code Context", replaced);
        Assert.DoesNotContain("- None yet.", replaced);
    }

    [Fact]
    public void ReplaceRulesSection_MissingRules_InsertsBeforeFirstSection()
    {
        var wm = """
            # Working Memory

            ## Current Goal
            Goal only.
            """;

        var replaced = WorkingMemoryRulesSection.ReplaceRulesSection(
            wm,
            WorkingMemoryRulesSection.FormatSection([
                new RuleBlock("inserted.md", "inserted.md", "Inserted", RuleSource.System)
            ]));

        Assert.Contains("### rule:inserted.md", replaced);
        var rulesIndex = replaced.IndexOf("## Rules", StringComparison.Ordinal);
        var goalIndex = replaced.IndexOf("## Current Goal", StringComparison.Ordinal);
        Assert.True(rulesIndex < goalIndex);
    }

    [Fact]
    public void FormatSection_EmptySet_UsesNoneYet()
    {
        var formatted = WorkingMemoryRulesSection.FormatSection([]);
        Assert.Equal("## Rules\n- None yet.\n", formatted);
    }
}
