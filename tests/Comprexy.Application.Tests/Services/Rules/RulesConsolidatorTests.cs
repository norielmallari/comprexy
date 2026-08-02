using Comprexy.Application.Services.Rules;
using Comprexy.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace Comprexy.Application.Tests.Services.Rules;

public class RulesConsolidatorTests
{
    private readonly RulesConsolidator _consolidator = new(NullLogger<RulesConsolidator>.Instance);

    [Fact]
    public void Consolidate_DedupesSameKey_SystemWinsOnConflict()
    {
        var system = new RuleBlock("rule-a", "rule-a", "system body", RuleSource.System);
        var transcript = new RuleBlock("rule-a", "rule-a", "transcript body", RuleSource.Transcript);

        var snapshot = _consolidator.Consolidate("base", [system], [transcript], null);

        Assert.Single(snapshot.AllRules);
        Assert.Equal("system body", snapshot.AllRules[0].Body);
        Assert.Single(snapshot.PendingRules);
    }

    [Fact]
    public void Consolidate_ReplaceSemantics_SecondTurnReplacesActiveSet()
    {
        var turn1 = _consolidator.Consolidate(
            "base",
            [new RuleBlock("old.md", "old.md", "old", RuleSource.System)],
            [],
            null);
        Assert.Single(turn1.AllRules);

        var turn2 = _consolidator.Consolidate(
            "base",
            [new RuleBlock("new.md", "new.md", "new", RuleSource.System)],
            [],
            null);

        Assert.Single(turn2.AllRules);
        Assert.Equal("new.md", turn2.AllRules[0].NormalizedKey);
    }

    [Fact]
    public void Consolidate_WithWorkingMemory_SplitsInWmAndPending()
    {
        var wmContent = """
            # Working Memory

            ## Rules
            ### rule:folded.md
            folded.md

            Folded body.
            """;
        var wm = WorkingMemory.Create(Guid.NewGuid(), 1, wmContent, 10, DateTimeOffset.UtcNow);

        var snapshot = _consolidator.Consolidate(
            "base",
            [
                new RuleBlock("folded.md", "folded.md", "Folded body.", RuleSource.System),
                new RuleBlock("pending.md", "pending.md", "Pending body.", RuleSource.System)
            ],
            [],
            wm);

        Assert.Contains("folded.md", snapshot.InWorkingMemoryKeys);
        Assert.Single(snapshot.PendingRules);
        Assert.Equal("pending.md", snapshot.PendingRules[0].NormalizedKey);
    }

    [Fact]
    public void Consolidate_BodyMismatchInWm_MarksPending()
    {
        var wmContent = """
            # Working Memory

            ## Rules
            ### rule:rule-a.md
            rule-a.md

            Stale body.
            """;
        var wm = WorkingMemory.Create(Guid.NewGuid(), 1, wmContent, 10, DateTimeOffset.UtcNow);

        var snapshot = _consolidator.Consolidate(
            "base",
            [new RuleBlock("rule-a.md", "rule-a.md", "Fresh body.", RuleSource.System)],
            [],
            wm);

        Assert.Empty(snapshot.InWorkingMemoryKeys);
        Assert.Single(snapshot.PendingRules);
    }
}
