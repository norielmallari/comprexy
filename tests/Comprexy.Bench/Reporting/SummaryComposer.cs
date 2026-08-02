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
            "Token figures use control-api Metrics:PromptTokenBasis=ProviderActual: per-turn prompt tokens prefer upstream usage.prompt_tokens when present (tiktoken estimate otherwise); completion stays usage.completion_tokens; both arms are projected the same way. \"Sent\" is what each arm's proxy forwarded upstream under that basis; the treatment arm is additionally charged its compression overhead. Proxy turn timing covers prepare, upstream, and persist for a turn — it is not the full agent loop, which includes local tool execution.");
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
            if (metrics.Survivals.Count == 0)
            {
                builder.AppendLine("No conversation completed on both arms, so there is no paired token comparison. Where one arm finished and the other did not, that difference in outcome is the result.");
                builder.AppendLine();
            }
        }
        else
        {
            builder.AppendLine("| Conversation | Prompts | Input (maf) | Output (maf) | Input (cx) | Output (cx) | Overhead (cx) | Saved | Reduction |");
            builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
            foreach (var pair in metrics.Paired)
            {
                builder.AppendLine(
                    $"| {pair.Name} | {pair.PromptCount} | {Format(pair.MafCompact.InputTokens)} | {Format(pair.MafCompact.OutputTokens)} | {Format(pair.Comprexy.InputTokens)} | {Format(pair.Comprexy.OutputTokens)} | {Format(pair.Comprexy.CompressionOverheadTokens)} | {Format(pair.TokensSavedVersusBaseline)} | {Percent(pair.TokenReductionRatio)} |");
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
                $"Across {metrics.Headline.PairedConversationCount} paired conversation(s): maf-compact sent {Format(metrics.Headline.PairedBaselineTokensEstimated)} tokens (input+output+overhead) versus comprexy {Format(metrics.Headline.PairedComprexyTokensEstimated)} — {Format(metrics.Headline.PairedTokensSaved)} tokens ({Percent(metrics.Headline.PairedTokenReductionRatio)}).");
            builder.AppendLine();

            if (metrics.Headline.PairedMafCompactProxyTurnDurationMs is { } baselineMs &&
                metrics.Headline.PairedComprexyProxyTurnDurationMs is { } treatmentMs)
            {
                builder.AppendLine(
                    $"Summed proxy turn time across the same conversations: {Seconds(baselineMs)} on `maf-compact` versus {Seconds(treatmentMs)} on `comprexy`.");
                builder.AppendLine();
            }
        }

        if (metrics.Survivals.Count > 0)
        {
            builder.AppendLine("### Survival past baseline kill zone");
            builder.AppendLine();
            builder.AppendLine(
                "Default harness behavior: when `maf-compact` dies of a provider/context failure after X prompts, `comprexy` stops once it completes X+margin (default margin 1). That is not a full-script token pair — clearing the kill zone is the result. Opt out at run time with `--continue-past-baseline-failure`.");
            builder.AppendLine();
            builder.AppendLine("| Conversation | Baseline end | Treatment stop | Peak sent full run (maf / cx) |");
            builder.AppendLine("| --- | --- | --- | ---: |");
            foreach (var survival in metrics.Survivals)
            {
                var baselineEnd =
                    $"{survival.BaselineStatus} @ {survival.BaselinePromptsCompleted}/{survival.PromptCount}";
                var treatmentStop =
                    $"{ConversationStatus.SurvivedBaselineFailure} @ {survival.TreatmentPromptsCompleted}/{survival.PromptCount}";
                var peakMaf = survival.MafCompact is { } m ? Format(m.PeakPromptTokensSent) : "n/a";
                var peakCx = survival.Comprexy is { } c ? Format(c.PeakPromptTokensSent) : "n/a";
                builder.AppendLine(
                    $"| {survival.Name} | {baselineEnd} | {treatmentStop} | {peakMaf} / {peakCx} |");
            }

            builder.AppendLine();
            var withPrefix = metrics.Survivals.Where(s => s.CommonPrefix is not null).ToList();
            if (withPrefix.Count > 0)
            {
                builder.AppendLine(
                    "Common completed prefix (prompts 1..X-1, where X is the baseline's erroring prompt): token totals from stored turns before the next script user message, projected with Metrics:PromptTokenBasis=ProviderActual (upstream usage when present), with Comprexy compression-event overhead in the same window charged against the treatment arm. Wall clock is first script user → start of prompt X (includes local tool time); proxy-turn ms is the sum of per-turn DurationMs in that window.");
                builder.AppendLine();
                builder.AppendLine(
                    "| Conversation | Prefix prompts | Sent (maf-compact) | Sent + overhead (comprexy) | Saved | Reduction | Peak sent (maf / cx) | Turns (maf / cx) | Wall clock (maf / cx) | Proxy turn ms (maf / cx) |");
                builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
                foreach (var survival in withPrefix)
                {
                    var p = survival.CommonPrefix!;
                    builder.AppendLine(
                        $"| {survival.Name} | 1..{p.CommonCompletedPrompts} (X={p.ErroringBaselinePrompt}) | {Format(p.MafCompactTokensSent)} | {Format(p.ComprexyTokensSentIncludingOverhead)} | {Format(p.TokensSavedVersusBaseline)} | {Percent(p.TokenReductionRatio)} | {Format(p.MafCompactPeakPromptTokensSent)} / {Format(p.ComprexyPeakPromptTokensSent)} | {p.MafCompactTurnCount} / {p.ComprexyTurnCount} | {Seconds(p.MafCompactWallClockMs)} / {Seconds(p.ComprexyWallClockMs)} | {Seconds(p.MafCompactProxyTurnDurationMs)} / {Seconds(p.ComprexyProxyTurnDurationMs)} |");
                }

                builder.AppendLine();
            }

            foreach (var survival in metrics.Survivals.Where(s => s.TreatmentDetail is not null))
            {
                builder.AppendLine($"`comprexy` / {survival.Name}: {survival.TreatmentDetail}");
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

        foreach (var survival in metrics.Survivals)
        {
            builder.AppendLine(
                $"- {survival.Name}: survival — baseline {survival.BaselineStatus} after {survival.BaselinePromptsCompleted}/{survival.PromptCount}; treatment stopped at {survival.TreatmentPromptsCompleted}/{survival.PromptCount} (`survived_baseline_failure`). Not a full-script token pair.");
            wrote = true;
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
        if (metrics.Survivals.Count > 0 && metrics.Paired.Count == 0)
        {
            var bits = metrics.Survivals.Select(s =>
            {
                var head =
                    $"{s.Name}: maf-compact {s.BaselineStatus} after {s.BaselinePromptsCompleted}/{s.PromptCount}, " +
                    $"comprexy {ConversationStatus.SurvivedBaselineFailure} at {s.TreatmentPromptsCompleted}/{s.PromptCount}";
                if (s.CommonPrefix is { } p)
                {
                    return head +
                           $"; common prefix prompts 1..{p.CommonCompletedPrompts}: maf-compact sent {Format(p.MafCompactTokensSent)} versus comprexy {Format(p.ComprexyTokensSentIncludingOverhead)} including overhead ({Percent(p.TokenReductionRatio)}, peak {Format(p.MafCompactPeakPromptTokensSent)} / {Format(p.ComprexyPeakPromptTokensSent)}, wall clock {Seconds(p.MafCompactWallClockMs)} / {Seconds(p.ComprexyWallClockMs)})";
                }

                return head;
            });
            return
                $"No full-script paired token comparison because survival early-stop ended the treatment arm after it cleared the baseline kill zone. " +
                string.Join("; ", bits) +
                ". Prefer the common-prefix figures when contrasting spend; the survival latch itself is the outcome asymmetry. " +
                "Narrative interpretation was not generated for this run (--no-agent).";
        }

        if (metrics.Paired.Count == 0)
        {
            var finished = metrics.Outcomes
                .Where(o => ConversationStatus.IsSuccessfulTerminal(o.Status))
                .Select(o => $"{o.Arm} ({o.Status})")
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var unfinished = metrics.Outcomes
                .Where(o => !ConversationStatus.IsSuccessfulTerminal(o.Status))
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

        var survivalNote = metrics.Survivals.Count > 0
            ? $" Separately, {metrics.Headline.SurvivalConversationCount} conversation(s) used survival early-stop rather than a full-script pair."
            : string.Empty;

        return
            $"On this workload the `comprexy` arm {direction} once compression overhead is charged against it." +
            survivalNote +
            " Narrative interpretation was not generated for this run (--no-agent).";
    }

    private static string Format(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Percent(double ratio) =>
        (ratio * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static string Seconds(long milliseconds) =>
        (milliseconds / 1000.0).ToString("N1", CultureInfo.InvariantCulture) + "s";

    private static string Short(string commit) => commit.Length >= 7 ? commit[..7] : commit;
}
