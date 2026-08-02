using System.Text.RegularExpressions;
using Comprexy.Application.Models;
using Comprexy.Domain.Enums;

namespace Comprexy.Application.Services.Rules;

public sealed partial class TranscriptRulesDetector : ITranscriptRulesDetector
{
    public IReadOnlyList<RuleBlock> Detect(IReadOnlyList<ChatMessage> newClientMessages)
    {
        var rules = new List<RuleBlock>();
        foreach (var message in newClientMessages)
        {
            if (message.Role is not (MessageRole.User or MessageRole.Tool))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(message.Content))
            {
                continue;
            }

            ExtractRulesXml(message.Content, rules);
            ExtractReadAppendix(message.Content, rules);
        }

        return rules;
    }

    private static void ExtractRulesXml(string content, List<RuleBlock> rules)
    {
        foreach (Match match in RulesXmlRegex().Matches(content))
        {
            var name = match.Groups["name"].Value.Trim();
            var body = match.Groups["body"].Value.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(body))
            {
                continue;
            }

            var key = WorkingMemoryRulesSection.NormalizeKey(name);
            rules.Add(new RuleBlock(key, name, body, RuleSource.Transcript));
        }
    }

    private static void ExtractReadAppendix(string content, List<RuleBlock> rules)
    {
        if (!content.Contains("The following cursor rule files are relevant", StringComparison.Ordinal))
        {
            return;
        }

        foreach (Match match in CursorRuleDelimiterRegex().Matches(content))
        {
            var path = match.Groups["path"].Value.Trim();
            var body = match.Groups["body"].Value.Trim();
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(body))
            {
                continue;
            }

            var key = WorkingMemoryRulesSection.NormalizeKey(path);
            rules.Add(new RuleBlock(key, key, body, RuleSource.Transcript));
        }
    }

    [GeneratedRegex(
        @"<rules>\s*<rule\s+name=""(?<name>[^""]+)""\s*>(?<body>.*?)</rule>\s*</rules>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex RulesXmlRegex();

    [GeneratedRegex(
        @"--- rule:\s*(?<path>[^\r\n]+)\s*---\s*(?:\r?\n)(?<body>.*?)(?=\r?\n--- rule:|\z)",
        RegexOptions.Singleline)]
    private static partial Regex CursorRuleDelimiterRegex();
}
