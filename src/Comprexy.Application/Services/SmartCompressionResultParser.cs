using System.Text.RegularExpressions;

namespace Comprexy.Application.Services;

/// <summary>
/// Parses Smart compression model output: markdown working memory plus an optional
/// <c>## Retain Sequences</c> section listing sequence ids.
/// </summary>
public static class SmartCompressionResultParser
{
    private static readonly Regex RetainSection = new(
        @"^[ \t]*##[ \t]+Retain[ \t]+Sequences[ \t]*\r?\n?(?<body>.*?)(?=^[ \t]*##\s|\z)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled |
        RegexOptions.Multiline | RegexOptions.Singleline);

    private static readonly Regex IntegerToken = new(
        @"\d+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static SmartCompressionParseResult Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return SmartCompressionParseResult.Empty;
        }

        var trimmed = content.Trim();
        var match = RetainSection.Match(trimmed);
        if (!match.Success)
        {
            return SmartCompressionParseResult.MarkdownOnly(trimmed);
        }

        var retainSequences = new List<int>();
        foreach (Match token in IntegerToken.Matches(match.Groups["body"].Value))
        {
            if (int.TryParse(token.Value, out var sequence))
            {
                retainSequences.Add(sequence);
            }
        }

        var workingMemory = trimmed
            .Remove(match.Index, match.Length)
            .Trim();

        if (string.IsNullOrWhiteSpace(workingMemory))
        {
            return SmartCompressionParseResult.Empty;
        }

        return new SmartCompressionParseResult(
            workingMemory,
            retainSequences,
            FoundRetainSection: true);
    }
}

public readonly record struct SmartCompressionParseResult(
    string WorkingMemory,
    IReadOnlyList<int>? RetainSequences,
    bool FoundRetainSection)
{
    public static SmartCompressionParseResult Empty { get; } =
        new(string.Empty, null, FoundRetainSection: false);

    public static SmartCompressionParseResult MarkdownOnly(string workingMemory) =>
        new(workingMemory.Trim(), null, FoundRetainSection: false);

    public bool HasWorkingMemory => !string.IsNullOrWhiteSpace(WorkingMemory);

    /// <summary>True when a Retain Sequences section was present (list may be empty).</summary>
    public bool HasRetainList => FoundRetainSection && RetainSequences is not null;
}
