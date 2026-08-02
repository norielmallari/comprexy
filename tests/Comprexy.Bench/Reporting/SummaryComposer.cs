using System.Globalization;
using System.Text;
using Comprexy.Bench.Model;

namespace Comprexy.Bench.Reporting;

/// <summary>
/// Renders the deterministic parts of <c>summary.md</c>. Every figure a reader sees comes from
/// <c>metrics.json</c> through this composer; the report agent only contributes the interpretation
/// section.
/// </summary>
internal static class SummaryComposer
{
    public static string ComposeNumbersBlock(BenchMetrics metrics)
    {
        var builder = new StringBuilder();

        builder.AppendLine("## Method");
        builder.AppendLine();
        builder.AppendLine(
            "Two arms replayed the same frozen prompt lists through Microsoft Agent Framework against a real Comprexy proxy, sequentially, with file and shell tools rooted at a throwaway workspace.");
        builder.AppendLine();
        if (metrics.Arms.Count == 0)
        {
            builder.AppendLine("No arm ran in this run.");
        }
        else
        {
            builder.AppendLine("| Arm | Comprexy | Client compaction |");
            builder.AppendLine("| --- | --- | --- |");
            foreach (var arm in metrics.Arms)
            {
                var clientCompaction = arm.ClientCompactionEnabled
                    ? $"MAF `MaxContextWindowTokens` {Format(metrics.Harness.MaxContextWindowTokens)}"
                    : "disabled";
                builder.AppendLine(
                    $"| `{arm.Name}` | `ToolSchema:Mode={arm.Resolved.ToolSchemaMode}`, soft limit {Format(arm.Resolved.SoftLimitTokens)} tokens | {clientCompaction} |");
            }
        }

        builder.AppendLine();
        builder.AppendLine(
            $"Model: `{metrics.Model ?? "resolved by the proxy"}`. Comprexy commit `{Short(metrics.ComprexyCommit)}`{(metrics.RepositoryDirty ? " (working tree dirty)" : string.Empty)}.");
        builder.AppendLine();
        builder.AppendLine(
            "Token figures are Comprexy's stored per-turn tiktoken estimates read back from control-api. \"Sent\" is what each arm's proxy forwarded upstream; the treatment arm is additionally charged its compression overhead. Proxy turn timing covers prepare, upstream, and persist for a turn — it is not the full agent loop, which includes local tool execution.");
        builder.AppendLine();

        builder.AppendLine("## Results");
        builder.AppendLine();

        if (metrics.Outcomes.Count > 0)
        {
            builder.AppendLine("| Arm | Conversation | Outcome | Prompts | Wall clock |");
            builder.AppendLine("| --- | --- | --- | ---: | ---: |");
            foreach (var outcome in metrics.Outcomes)
            {
                builder.AppendLine(
                    $"| `{outcome.Arm}` | {outcome.Name} | {outcome.Status} | {outcome.PromptsCompleted}/{outcome.PromptCount} | {Seconds(outcome.ConversationWallClockMs)} |");
            }

            builder.AppendLine();

            foreach (var stalled in metrics.Outcomes.Where(o => o.FailureReason is not null))
            {
                builder.AppendLine($"`{stalled.Arm}` / {stalled.Name}: {stalled.FailureReason}.");
                builder.AppendLine();
            }
        }

        if (metrics.Paired.Count == 0)
        {
            builder.AppendLine("No conversation completed on both arms, so there is no paired token comparison. Where one arm finished and the other did not, that difference in outcome is the result.");
            builder.AppendLine();
        }
        else
        {
            builder.AppendLine("| Conversation | Prompts | Sent (maf-compact) | Sent + overhead (comprexy) | Saved | Reduction |");
            builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");
            foreach (var pair in metrics.Paired)
            {
                var treatmentCost = pair.Comprexy.CompressedTokensEstimated + pair.Comprexy.CompressionOverheadTokens;
                builder.AppendLine(
                    $"| {pair.Name} | {pair.PromptCount} | {Format(pair.MafCompact.CompressedTokensEstimated)} | {Format(treatmentCost)} | {Format(pair.TokensSavedVersusBaseline)} | {Percent(pair.TokenReductionRatio)} |");
            }

            builder.AppendLine();
            builder.AppendLine("| Conversation | Turns (maf / cx) | Peak sent (maf / cx) | Peak raw (maf / cx) |");
            builder.AppendLine("| --- | ---: | ---: | ---: |");
            foreach (var pair in metrics.Paired)
            {
                builder.AppendLine(
                    $"| {pair.Name} | {pair.MafCompact.TurnCount} / {pair.Comprexy.TurnCount} | {Format(pair.MafCompact.PeakPromptTokensSent)} / {Format(pair.Comprexy.PeakPromptTokensSent)} | {Format(pair.MafCompact.PeakRawPromptTokensEstimated)} / {Format(pair.Comprexy.PeakRawPromptTokensEstimated)} |");
            }

            builder.AppendLine();
            builder.AppendLine(
                $"Across {metrics.Headline.PairedConversationCount} paired conversation(s): {Format(metrics.Headline.PairedBaselineTokensEstimated)} tokens sent on `maf-compact` versus {Format(metrics.Headline.PairedComprexyTokensEstimated)} on `comprexy` including overhead — {Format(metrics.Headline.PairedTokensSaved)} tokens ({Percent(metrics.Headline.PairedTokenReductionRatio)}).");
            builder.AppendLine();

            if (metrics.Headline.PairedMafCompactProxyTurnDurationMs is { } baselineMs &&
                metrics.Headline.PairedComprexyProxyTurnDurationMs is { } treatmentMs)
            {
                builder.AppendLine(
                    $"Summed proxy turn time across the same conversations: {Seconds(baselineMs)} on `maf-compact` versus {Seconds(treatmentMs)} on `comprexy`.");
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    public static string ComposeCaveats(BenchMetrics metrics)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Caveats");
        builder.AppendLine();

        var wrote = false;

        foreach (var pair in metrics.Paired.Where(p => p.Caveats.Count > 0))
        {
            foreach (var caveat in pair.Caveats)
            {
                builder.AppendLine($"- {pair.Name}: {caveat}");
                wrote = true;
            }
        }

        foreach (var excluded in metrics.Excluded)
        {
            builder.AppendLine($"- {excluded.Name}: excluded — {excluded.Reason}");
            wrote = true;
        }

        if (metrics.RepositoryDirty)
        {
            builder.AppendLine("- The working tree was dirty at run time, so the commit alone does not pin the code under test.");
            wrote = true;
        }

        if (!wrote)
        {
            builder.AppendLine("- None recorded for this run.");
        }

        builder.AppendLine();
        builder.AppendLine(
            "Single local run, one model, one machine: treat these as directional figures for this workload rather than a general benchmark.");
        builder.AppendLine();

        return builder.ToString();
    }

    public static string ComposeDocument(BenchMetrics metrics, string interpretation)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# Bench run {metrics.RunId}");
        builder.AppendLine();
        builder.AppendLine(
            $"Generated {metrics.GeneratedAt.ToString("u", CultureInfo.InvariantCulture)} from `reports/bench/{metrics.RunId}/metrics.json`.");
        builder.AppendLine();
        builder.Append(ComposeNumbersBlock(metrics));
        builder.AppendLine("## Interpretation");
        builder.AppendLine();
        builder.AppendLine(interpretation.Trim());
        builder.AppendLine();
        builder.Append(ComposeCaveats(metrics));
        return builder.ToString();
    }

    public static string ComposeDeterministicInterpretation(BenchMetrics metrics)
    {
        if (metrics.Paired.Count == 0)
        {
            var finished = metrics.Outcomes
                .Where(o => o.Status == ConversationStatus.Completed)
                .Select(o => o.Arm)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var unfinished = metrics.Outcomes
                .Where(o => o.Status != ConversationStatus.Completed)
                .Select(o => $"{o.Arm} ({o.Status})")
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return unfinished.Count > 0
                ? $"No paired token comparison: {string.Join(", ", unfinished)} did not finish" +
                  (finished.Count > 0 ? $", while {string.Join(", ", finished)} did" : string.Empty) +
                  ". Completing a workload the other arm could not is itself the result. " +
                  "Narrative interpretation was not generated for this run (--no-agent)."
                : "No paired conversations, so this run supports no comparison. See the caveats for why each conversation was excluded.";
        }

        var direction = metrics.Headline.PairedTokensSaved switch
        {
            > 0 => "sent fewer tokens upstream than the baseline arm",
            < 0 => "sent more tokens upstream than the baseline arm",
            _ => "sent the same number of tokens upstream as the baseline arm"
        };

        return
            $"On this workload the `comprexy` arm {direction} once compression overhead is charged against it. " +
            "Narrative interpretation was not generated for this run (--no-agent).";
    }

    private static string Format(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Percent(double ratio) =>
        (ratio * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static string Seconds(long milliseconds) =>
        (milliseconds / 1000.0).ToString("N1", CultureInfo.InvariantCulture) + "s";

    private static string Short(string commit) => commit.Length >= 7 ? commit[..7] : commit;
}
