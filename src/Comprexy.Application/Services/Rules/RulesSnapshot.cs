namespace Comprexy.Application.Services.Rules;

public sealed class RulesSnapshot
{
    public RulesSnapshot(
        string baseSystem,
        IReadOnlyList<RuleBlock> allRules,
        IReadOnlySet<string> inWorkingMemoryKeys,
        IReadOnlyList<RuleBlock> pendingRules)
    {
        BaseSystem = baseSystem;
        AllRules = allRules;
        InWorkingMemoryKeys = inWorkingMemoryKeys;
        PendingRules = pendingRules;
    }

    public string BaseSystem { get; }

    public IReadOnlyList<RuleBlock> AllRules { get; }

    public IReadOnlySet<string> InWorkingMemoryKeys { get; }

    public IReadOnlyList<RuleBlock> PendingRules { get; }

    public string FormatForWorkingMemory() =>
        WorkingMemoryRulesSection.FormatSection(AllRules);
}
