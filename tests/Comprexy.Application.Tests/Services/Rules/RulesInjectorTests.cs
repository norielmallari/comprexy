using Comprexy.Application.Services.Rules;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Tests.Services.Rules;

public class RulesInjectorTests
{
    private readonly RulesInjector _injector = new();

    [Fact]
    public void BuildPendingMessages_NoWorkingMemory_EmitsAllRules()
    {
        var snapshot = new RulesSnapshot(
            "base",
            [
                new RuleBlock("a.md", "a.md", "A", RuleSource.System),
                new RuleBlock("b.md", "b.md", "B", RuleSource.System)
            ],
            new HashSet<string>(),
            [
                new RuleBlock("a.md", "a.md", "A", RuleSource.System),
                new RuleBlock("b.md", "b.md", "B", RuleSource.System)
            ]);

        var messages = _injector.BuildPendingMessages(snapshot, hasWorkingMemory: false);

        Assert.Equal(2, messages.Count);
        Assert.All(messages, m => Assert.Equal(MessageRole.System, m.Role));
    }

    [Fact]
    public void BuildPendingMessages_WithWorkingMemory_EmitsPendingOnly()
    {
        var snapshot = new RulesSnapshot(
            "base",
            [
                new RuleBlock("a.md", "a.md", "A", RuleSource.System),
                new RuleBlock("b.md", "b.md", "B", RuleSource.System)
            ],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a.md" },
            [new RuleBlock("b.md", "b.md", "B", RuleSource.System)]);

        var messages = _injector.BuildPendingMessages(snapshot, hasWorkingMemory: true);

        Assert.Single(messages);
        Assert.Contains("b.md", messages[0].Content);
    }

    [Fact]
    public void BuildPendingMessages_EmptyPending_ReturnsEmpty()
    {
        var snapshot = new RulesSnapshot(
            "base",
            [new RuleBlock("a.md", "a.md", "A", RuleSource.System)],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a.md" },
            []);

        Assert.Empty(_injector.BuildPendingMessages(snapshot, hasWorkingMemory: true));
    }
}
