using Comprexy.Application.Models;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Services.Rules;

public sealed class RulesInjector : IRulesInjector
{
    public IReadOnlyList<ChatMessage> BuildPendingMessages(RulesSnapshot snapshot, bool hasWorkingMemory)
    {
        var source = hasWorkingMemory ? snapshot.PendingRules : snapshot.AllRules;
        if (source.Count == 0)
        {
            return Array.Empty<ChatMessage>();
        }

        return source
            .OrderBy(r => r.NormalizedKey, StringComparer.OrdinalIgnoreCase)
            .Select(FormatRuleMessage)
            .ToList();
    }

    private static ChatMessage FormatRuleMessage(RuleBlock rule)
    {
        var content = $"[Rule: {rule.NormalizedKey}] {rule.Title}\n\n{rule.Body.TrimEnd()}";
        return new ChatMessage(MessageRole.System, content);
    }
}
