using System.Text;
using System.Text.RegularExpressions;

namespace Comprexy.Application.Services.Rules;

public static partial class WorkingMemoryRulesSection
{
    private const string RulesHeading = "## Rules";

    public static string NormalizeKey(string rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return string.Empty;
        }

        var trimmed = rawKey.Trim();
        var basename = trimmed.Replace('\\', '/');
        var lastSlash = basename.LastIndexOf('/');
        if (lastSlash >= 0 && lastSlash < basename.Length - 1)
        {
            basename = basename[(lastSlash + 1)..];
        }

        return basename.ToLowerInvariant();
    }

    public static IReadOnlyDictionary<string, RuleBlock> TryParse(string? wmContent)
    {
        if (string.IsNullOrWhiteSpace(wmContent))
        {
            return new Dictionary<string, RuleBlock>();
        }

        var rulesIndex = wmContent.IndexOf(RulesHeading, StringComparison.Ordinal);
        if (rulesIndex < 0)
        {
            return new Dictionary<string, RuleBlock>();
        }

        var afterHeading = wmContent[(rulesIndex + RulesHeading.Length)..];
        var nextSection = afterHeading.IndexOf("\n## ", StringComparison.Ordinal);
        var rulesBody = nextSection >= 0 ? afterHeading[..nextSection] : afterHeading;

        if (rulesBody.Contains("- None yet.", StringComparison.Ordinal))
        {
            return new Dictionary<string, RuleBlock>();
        }

        var rules = new Dictionary<string, RuleBlock>();
        var matches = RuleHeadingRegex().Matches(rulesBody);
        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var key = NormalizeKey(match.Groups["key"].Value);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var start = match.Index + match.Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : rulesBody.Length;
            var block = rulesBody[start..end].Trim();
            var newline = block.IndexOf('\n');
            var title = newline >= 0 ? block[..newline].Trim() : block;
            var body = newline >= 0 ? block[(newline + 1)..].Trim() : string.Empty;

            rules[key] = new RuleBlock(key, title, body, RuleSource.System);
        }

        return rules;
    }

    public static string FormatSection(IEnumerable<RuleBlock> rules)
    {
        var ordered = rules
            .OrderBy(r => r.NormalizedKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ordered.Count == 0)
        {
            return $"{RulesHeading}\n- None yet.\n";
        }

        var builder = new StringBuilder();
        builder.AppendLine(RulesHeading);
        foreach (var rule in ordered)
        {
            builder.AppendLine($"### rule:{rule.NormalizedKey}");
            builder.AppendLine(rule.Title);
            builder.AppendLine();
            builder.AppendLine(rule.Body.TrimEnd());
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd() + "\n";
    }

    public static string ReplaceRulesSection(string wmMarkdown, string rulesSectionBody)
    {
        if (string.IsNullOrWhiteSpace(rulesSectionBody))
        {
            rulesSectionBody = FormatSection([]);
        }

        var normalizedBody = rulesSectionBody.TrimEnd();
        var rulesIndex = wmMarkdown.IndexOf(RulesHeading, StringComparison.Ordinal);
        if (rulesIndex >= 0)
        {
            var afterHeading = wmMarkdown[(rulesIndex + RulesHeading.Length)..];
            var nextSection = afterHeading.IndexOf("\n## ", StringComparison.Ordinal);
            var before = wmMarkdown[..rulesIndex];
            var after = nextSection >= 0 ? afterHeading[(nextSection + 1)..] : string.Empty;
            var merged = before + normalizedBody;
            if (!string.IsNullOrEmpty(after))
            {
                if (!merged.EndsWith('\n'))
                {
                    merged += "\n";
                }

                merged += after;
            }

            return merged;
        }

        var workingMemoryIndex = wmMarkdown.IndexOf("# Working Memory", StringComparison.Ordinal);
        if (workingMemoryIndex >= 0)
        {
            var afterWm = wmMarkdown[(workingMemoryIndex + "# Working Memory".Length)..];
            var firstSection = afterWm.IndexOf("\n## ", StringComparison.Ordinal);
            if (firstSection >= 0)
            {
                var insertAt = workingMemoryIndex + "# Working Memory".Length + firstSection;
                return wmMarkdown[..insertAt]
                    + "\n"
                    + normalizedBody
                    + wmMarkdown[insertAt..];
            }
        }

        var prefix = wmMarkdown.TrimEnd();
        if (!prefix.EndsWith('\n'))
        {
            prefix += "\n\n";
        }
        else if (!prefix.EndsWith("\n\n", StringComparison.Ordinal))
        {
            prefix += "\n";
        }

        return prefix + normalizedBody + "\n";
    }

    [GeneratedRegex(@"^### rule:(?<key>[^\r\n]+)\s*$", RegexOptions.Multiline)]
    private static partial Regex RuleHeadingRegex();
}
