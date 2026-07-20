using System.Text.RegularExpressions;

namespace Comprexy.Application.Services;

/// <summary>
/// Structural sanity check for compression-model working memory output.
/// Expects the prompt contract: a <c># Working Memory</c> document with at least one <c>##</c> section.
/// </summary>
public static class WorkingMemorySanityChecker
{
    private static readonly Regex OuterFence = new(
        @"^```(?:markdown|md)?[ \t]*\r?\n(?<body>.*?)\r?\n```[ \t]*\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled |
        RegexOptions.Singleline);

    private static readonly Regex WorkingMemoryHeading = new(
        @"^[ \t]*#[ \t]+Working[ \t]+Memory\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled |
        RegexOptions.Multiline);

    private static readonly Regex SectionHeading = new(
        @"^[ \t]*##[ \t]+\S",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Returns true when <paramref name="content"/> is structurally usable working memory.
    /// On success, <paramref name="normalized"/> is trimmed (and outer markdown fences stripped).
    /// </summary>
    public static bool TryAccept(string? content, out string normalized, out string rejectionReason)
    {
        normalized = string.Empty;
        rejectionReason = string.Empty;

        if (string.IsNullOrWhiteSpace(content))
        {
            rejectionReason = "empty";
            return false;
        }

        var candidate = UnwrapOuterFence(content.Trim());
        if (string.IsNullOrWhiteSpace(candidate))
        {
            rejectionReason = "empty_after_normalize";
            return false;
        }

        if (!WorkingMemoryHeading.IsMatch(candidate))
        {
            rejectionReason = "missing_working_memory_heading";
            return false;
        }

        if (!SectionHeading.IsMatch(candidate))
        {
            rejectionReason = "missing_section_heading";
            return false;
        }

        normalized = candidate;
        return true;
    }

    private static string UnwrapOuterFence(string content)
    {
        var match = OuterFence.Match(content);
        return match.Success ? match.Groups["body"].Value.Trim() : content;
    }
}
