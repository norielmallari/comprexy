using Comprexy.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Comprexy.Application.Services.Rules;

public sealed class RulesConsolidator : IRulesConsolidator
{
    private readonly ILogger<RulesConsolidator> _logger;

    public RulesConsolidator(ILogger<RulesConsolidator> logger)
    {
        _logger = logger;
    }

    public RulesSnapshot Consolidate(
        string baseSystem,
        IReadOnlyList<RuleBlock> systemRules,
        IReadOnlyList<RuleBlock> transcriptRules,
        WorkingMemory? workingMemory)
    {
        var active = new Dictionary<string, RuleBlock>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in transcriptRules)
        {
            if (!string.IsNullOrWhiteSpace(rule.NormalizedKey))
            {
                active[rule.NormalizedKey] = rule;
            }
        }

        foreach (var rule in systemRules)
        {
            if (string.IsNullOrWhiteSpace(rule.NormalizedKey))
            {
                continue;
            }

            if (active.TryGetValue(rule.NormalizedKey, out var existing)
                && !string.Equals(existing.Body, rule.Body, StringComparison.Ordinal)
                && existing.Source != RuleSource.System)
            {
                _logger.LogWarning(
                    "Rule body conflict for key {RuleKey}; preferring system source.",
                    rule.NormalizedKey);
            }

            active[rule.NormalizedKey] = rule with { Source = RuleSource.System };
        }

        var wmRules = workingMemory is null
            ? new Dictionary<string, RuleBlock>()
            : WorkingMemoryRulesSection.TryParse(workingMemory.Content);

        var inWorkingMemory = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new List<RuleBlock>();

        foreach (var rule in active.Values.OrderBy(r => r.NormalizedKey, StringComparer.OrdinalIgnoreCase))
        {
            if (wmRules.TryGetValue(rule.NormalizedKey, out var wmRule)
                && string.Equals(wmRule.Body, rule.Body, StringComparison.Ordinal))
            {
                inWorkingMemory.Add(rule.NormalizedKey);
            }
            else
            {
                pending.Add(rule);
            }
        }

        var allRules = active.Values
            .OrderBy(r => r.NormalizedKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.LogTrace(
            "Rules consolidated: active={ActiveCount} inWorkingMemory={InWmCount} pending={PendingCount}",
            allRules.Count,
            inWorkingMemory.Count,
            pending.Count);

        return new RulesSnapshot(
            baseSystem,
            allRules,
            inWorkingMemory,
            pending);
    }
}
