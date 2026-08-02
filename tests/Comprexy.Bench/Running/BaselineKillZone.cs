using System.Text.RegularExpressions;
using Comprexy.Bench.Model;

namespace Comprexy.Bench.Running;

/// <summary>
/// Decides when a baseline arm death is a provider/context kill zone the treatment arm may
/// early-stop against, and how many treatment prompts count as clearing that zone.
/// </summary>
internal static class BaselineKillZone
{
    private static readonly Regex ProviderOrContextDeath = new(
        @"502|upstream_error|context[_\s-]?length|maximum\s+context|too\s+many\s+tokens|context\s+window|prompt\s+is\s+too\s+long",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// True when the baseline ended in a way that means the upstream could not serve the growing
    /// prompt — not a harness bug, auth miss, or missing conversation id.
    /// </summary>
    public static bool IsProviderOrContextDeath(BenchConversationRun run)
    {
        if (run.Status == ConversationStatus.CompletionStalled)
        {
            return true;
        }

        if (run.Status != ConversationStatus.Failed)
        {
            return false;
        }

        return run.FailureReason is { Length: > 0 } reason && ProviderOrContextDeath.IsMatch(reason);
    }

    /// <summary>
    /// Prompt count at which the treatment arm should stop (inclusive). Null when survival
    /// early-stop does not apply.
    /// </summary>
    public static int? SurvivalStopAfterPrompts(BenchConversationRun baseline, int marginPrompts)
    {
        if (!IsProviderOrContextDeath(baseline))
        {
            return null;
        }

        if (baseline.PromptsCompleted >= baseline.PromptCount)
        {
            return null;
        }

        marginPrompts = Math.Max(1, marginPrompts);
        return Math.Min(baseline.PromptCount, baseline.PromptsCompleted + marginPrompts);
    }

    public static string FormatSurvivalReason(
        BenchConversationRun baseline,
        int treatmentPromptsCompleted,
        int treatmentPromptCount,
        int marginPrompts) =>
        $"survived past baseline kill point: maf-compact ended {baseline.Status} after " +
        $"{baseline.PromptsCompleted}/{baseline.PromptCount} prompts" +
        (baseline.FailureReason is { Length: > 0 } r ? $" ({Summarize(r)})" : string.Empty) +
        $"; comprexy stopped after {treatmentPromptsCompleted}/{treatmentPromptCount} by harness " +
        $"survival early-stop (margin {Math.Max(1, marginPrompts)}). " +
        "Use --continue-past-baseline-failure for a full script.";

    private static string Summarize(string reason)
    {
        var oneLine = reason.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length <= 180 ? oneLine : oneLine[..177] + "...";
    }
}

/// <summary>Optional stop latch for the treatment arm after the baseline died in the kill zone.</summary>
internal sealed record SurvivalEarlyStop(
    int StopAfterPrompts,
    BenchConversationRun Baseline,
    int MarginPrompts);
