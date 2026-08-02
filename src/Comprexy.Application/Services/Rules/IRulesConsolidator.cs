using Comprexy.Domain.Entities;

namespace Comprexy.Application.Services.Rules;

public interface IRulesConsolidator
{
    RulesSnapshot Consolidate(
        string baseSystem,
        IReadOnlyList<RuleBlock> systemRules,
        IReadOnlyList<RuleBlock> transcriptRules,
        WorkingMemory? workingMemory);
}
