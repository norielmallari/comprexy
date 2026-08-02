using System.Text;
using System.Text.RegularExpressions;

namespace Comprexy.Application.Services.Rules;

public sealed partial class SystemRulesDetector : ISystemRulesDetector
{
    public (string BaseSystem, IReadOnlyList<RuleBlock> Rules) Detect(string? systemContent)
    {
        if (string.IsNullOrWhiteSpace(systemContent))
        {
            return (systemContent ?? string.Empty, Array.Empty<RuleBlock>());
        }

        var rules = new List<RuleBlock>();
        var removals = new List<(int Start, int End)>();

        ExtractKiloBlocks(systemContent, rules, removals);
        ExtractCursorGlobBlocks(systemContent, rules, removals);
        ExtractAlwaysAppliedBlocks(systemContent, rules, removals);

        var baseSystem = RemoveSpans(systemContent, removals);
        return (baseSystem.Trim(), rules);
    }

    private static void ExtractKiloBlocks(
        string content,
        List<RuleBlock> rules,
        List<(int Start, int End)> removals)
    {
        foreach (Match match in KiloInstructionsRegex().Matches(content))
        {
            var path = match.Groups["path"].Value.Trim();
            var body = match.Groups["body"].Value.Trim();
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(body))
            {
                continue;
            }

            AddRule(rules, path, body, removals, match.Index, match.Length);
        }
    }

    private static void ExtractCursorGlobBlocks(
        string content,
        List<RuleBlock> rules,
        List<(int Start, int End)> removals)
    {
        foreach (Match match in CursorGlobRegex().Matches(content))
        {
            var path = match.Groups["path"].Value.Trim();
            var body = match.Groups["body"].Value.Trim();
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(body))
            {
                continue;
            }

            AddRule(rules, path, body, removals, match.Index, match.Length);
        }
    }

    private static void ExtractAlwaysAppliedBlocks(
        string content,
        List<RuleBlock> rules,
        List<(int Start, int End)> removals)
    {
        foreach (Match wrapper in AlwaysAppliedWrapperRegex().Matches(content))
        {
            var inner = wrapper.Groups["inner"].Value;
            foreach (Match match in CursorRuleDelimiterRegex().Matches(inner))
            {
                var path = match.Groups["path"].Value.Trim();
                var body = match.Groups["body"].Value.Trim();
                if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(body))
                {
                    continue;
                }

                var key = WorkingMemoryRulesSection.NormalizeKey(path);
                rules.Add(new RuleBlock(key, TitleFromPath(path), body, RuleSource.System));
            }

            removals.Add((wrapper.Index, wrapper.Index + wrapper.Length));
        }
    }

    private static void AddRule(
        List<RuleBlock> rules,
        string path,
        string body,
        List<(int Start, int End)> removals,
        int index,
        int length)
    {
        var key = WorkingMemoryRulesSection.NormalizeKey(path);
        rules.Add(new RuleBlock(key, TitleFromPath(path), body, RuleSource.System));
        removals.Add((index, index + length));
    }

    private static string TitleFromPath(string path) =>
        WorkingMemoryRulesSection.NormalizeKey(path);

    private static string RemoveSpans(string content, List<(int Start, int End)> removals)
    {
        if (removals.Count == 0)
        {
            return content;
        }

        removals.Sort((a, b) => a.Start.CompareTo(b.Start));
        var builder = new StringBuilder();
        var cursor = 0;
        foreach (var (start, end) in removals)
        {
            if (start < cursor)
            {
                continue;
            }

            builder.Append(content[cursor..start]);
            cursor = end;
        }

        builder.Append(content[cursor..]);
        return builder.ToString();
    }

    [GeneratedRegex(
        @"Instructions from:\s*(?<path>[^\r\n]+)\s*(?:\r?\n)(?<body>.*?)(?=\r?\nInstructions from:|\z)",
        RegexOptions.Singleline)]
    private static partial Regex KiloInstructionsRegex();

    [GeneratedRegex(
        @"glob pattern\(s\) for applicable files:\s*[^\r\n]+\s*(?:\r?\n)+--- rule:\s*(?<path>[^\r\n]+)\s*---\s*(?:\r?\n)(?<body>.*?)(?=\r?\nglob pattern\(s\) for applicable files:|\z)",
        RegexOptions.Singleline)]
    private static partial Regex CursorGlobRegex();

    [GeneratedRegex(
        @"<always_applied_workspace_rules>(?<inner>.*?)</always_applied_workspace_rules>",
        RegexOptions.Singleline)]
    private static partial Regex AlwaysAppliedWrapperRegex();

    [GeneratedRegex(
        @"--- rule:\s*(?<path>[^\r\n]+)\s*---\s*(?:\r?\n)(?<body>.*?)(?=\r?\n--- rule:|\z)",
        RegexOptions.Singleline)]
    private static partial Regex CursorRuleDelimiterRegex();
}
