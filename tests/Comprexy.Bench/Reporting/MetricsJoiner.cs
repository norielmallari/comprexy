using Comprexy.Bench.Hosting;
using Comprexy.Bench.Model;

namespace Comprexy.Bench.Reporting;

/// <summary>
/// Builds <c>metrics.json</c> from the manifest plus the bench control-api.
///
/// Cross-arm savings compare the tokens each arm actually sent upstream (the Comprexy turn
/// ledger's compressed totals), with the treatment arm's compression overhead charged against it.
/// Pairing is deterministic and lives here, not in the report agent.
/// </summary>
internal static class MetricsJoiner
{
    public static async Task<BenchMetrics> BuildAsync(
        BenchManifest manifest,
        ControlApiClient controlApi,
        CancellationToken cancellationToken)
    {
        var arms = manifest.Arms.ToDictionary(a => a.Name, StringComparer.Ordinal);
        var excluded = new List<BenchExcludedConversation>();
        var paired = new List<BenchPairedConversation>();

        if (!arms.TryGetValue(BenchArm.MafCompact, out var baselineArm) ||
            !arms.TryGetValue(BenchArm.Comprexy, out var treatmentArm))
        {
            foreach (var conversation in manifest.Arms.SelectMany(a => a.Conversations))
            {
                excluded.Add(new BenchExcludedConversation(
                    conversation.Name,
                    "run covered a single arm; a paired comparison needs both maf-compact and comprexy"));
            }

            return Assemble(manifest, paired, excluded);
        }

        var treatmentByName = treatmentArm.Conversations.ToDictionary(c => c.Name, StringComparer.Ordinal);

        foreach (var baseline in baselineArm.Conversations)
        {
            if (!treatmentByName.TryGetValue(baseline.Name, out var treatment))
            {
                excluded.Add(new BenchExcludedConversation(baseline.Name, "not run on the comprexy arm"));
                continue;
            }

            if (!string.Equals(baseline.PromptListHash, treatment.PromptListHash, StringComparison.Ordinal))
            {
                excluded.Add(new BenchExcludedConversation(
                    baseline.Name, "prompt-list hash differs between arms"));
                continue;
            }

            if (baseline.Status != ConversationStatus.Completed || treatment.Status != ConversationStatus.Completed)
            {
                excluded.Add(new BenchExcludedConversation(
                    baseline.Name,
                    $"terminal status maf-compact={baseline.Status}, comprexy={treatment.Status}" +
                    DescribeFailure("maf-compact", baseline) + DescribeFailure("comprexy", treatment)));
                continue;
            }

            var baselineMetrics = await LoadAsync(controlApi, baseline, cancellationToken);
            var treatmentMetrics = await LoadAsync(controlApi, treatment, cancellationToken);

            if (baselineMetrics is null || treatmentMetrics is null)
            {
                excluded.Add(new BenchExcludedConversation(
                    baseline.Name,
                    "no stored turn metrics on the bench control-api for one or both arms"));
                continue;
            }

            var treatmentCost = treatmentMetrics.CompressedTokensEstimated + treatmentMetrics.CompressionOverheadTokens;
            var saved = baselineMetrics.CompressedTokensEstimated - treatmentCost;
            var ratio = baselineMetrics.CompressedTokensEstimated > 0
                ? Math.Round((double)saved / baselineMetrics.CompressedTokensEstimated, 6)
                : 0d;

            paired.Add(new BenchPairedConversation(
                baseline.Name,
                baseline.PromptListHash,
                baseline.PromptCount,
                baselineMetrics,
                treatmentMetrics,
                saved,
                ratio,
                BuildCaveats(baselineMetrics, treatmentMetrics)));
        }

        foreach (var treatmentOnly in treatmentArm.Conversations
                     .Where(c => baselineArm.Conversations.All(b => b.Name != c.Name)))
        {
            excluded.Add(new BenchExcludedConversation(treatmentOnly.Name, "not run on the maf-compact arm"));
        }

        return Assemble(manifest, paired, excluded);
    }

    private static string DescribeFailure(string armName, BenchConversationRun run) =>
        run.FailureReason is null ? string.Empty : $"; {armName}: {run.FailureReason}";

    private static IReadOnlyList<string> BuildCaveats(
        BenchConversationMetrics baseline,
        BenchConversationMetrics treatment)
    {
        var caveats = new List<string>();

        if (treatment.ClientCompactionCount is > 0)
        {
            caveats.Add(
                $"MAF client compaction fired {treatment.ClientCompactionCount}x on the comprexy arm: the prompt still reached the client window despite Comprexy compression");
        }

        if (baseline.TurnCount != treatment.TurnCount)
        {
            caveats.Add(
                $"turn counts differ ({baseline.TurnCount} vs {treatment.TurnCount}); the agent took a different number of tool hops per arm");
        }

        if (treatment.CompressionEventCount == 0)
        {
            caveats.Add("no Comprexy compression event fired; the conversation stayed under the soft limit");
        }

        return caveats;
    }

    private static async Task<BenchConversationMetrics?> LoadAsync(
        ControlApiClient controlApi,
        BenchConversationRun run,
        CancellationToken cancellationToken)
    {
        if (run.ConversationId is not { } conversationId)
        {
            return null;
        }

        var summary = await controlApi.GetSummaryAsync(conversationId, cancellationToken);
        if (summary is null || summary.TotalTurns == 0)
        {
            return null;
        }

        var turns = await controlApi.GetTurnsAsync(conversationId, cancellationToken);
        var finalTurn = turns.Count > 0 ? turns[^1] : null;

        return new BenchConversationMetrics(
            conversationId,
            summary.TotalTurns,
            summary.TotalBaselineTokensEstimated,
            summary.TotalActualTokensEstimated,
            summary.TotalNetTokensSaved,
            summary.AverageTokenSavingsRatio,
            summary.TotalCompressionOverheadTokens,
            summary.CompressionEventCount,
            finalTurn?.BaselineTotalTokensEstimated ?? 0,
            finalTurn?.CompressedTotalTokensEstimated ?? 0,
            turns.Count == 0 ? 0 : turns.Max(t => t.CompressedInputTokensEstimated),
            turns.Count == 0 ? 0 : turns.Max(t => t.RawInputTokensEstimated),
            SumDurations(turns, t => t.DurationMs),
            SumDurations(turns, t => t.UpstreamDurationMs),
            SumDurations(turns, t => t.PrepareDurationMs),
            run.ConversationWallClockMs,
            run.ClientCompactionCount);
    }

    private static long? SumDurations(
        IReadOnlyList<ConversationTurnResponse> turns,
        Func<ConversationTurnResponse, int?> selector)
    {
        var values = turns.Select(selector).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return values.Count == 0 ? null : values.Sum(v => (long)v);
    }

    private static BenchMetrics Assemble(
        BenchManifest manifest,
        IReadOnlyList<BenchPairedConversation> paired,
        IReadOnlyList<BenchExcludedConversation> excluded)
    {
        var baselineTokens = paired.Sum(p => p.MafCompact.CompressedTokensEstimated);
        var treatmentTokens = paired.Sum(p => p.Comprexy.CompressedTokensEstimated + p.Comprexy.CompressionOverheadTokens);
        var saved = baselineTokens - treatmentTokens;

        return new BenchMetrics
        {
            RunId = manifest.RunId,
            GeneratedAt = DateTimeOffset.UtcNow,
            ComprexyCommit = manifest.ComprexyCommit,
            RepositoryDirty = manifest.RepositoryDirty,
            Model = manifest.Model,
            Harness = manifest.Harness,
            Arms = manifest.Arms
                .Select(a => new BenchArmSettingsSnapshot(
                    a.Name, a.Description, a.ClientCompactionEnabled, a.Resolved, a.ArmWallClockMs))
                .ToList(),
            Outcomes = manifest.Arms
                .SelectMany(a => a.Conversations.Select(c => new BenchConversationOutcome(
                    a.Name,
                    c.Name,
                    c.Status,
                    c.PromptsCompleted,
                    c.PromptCount,
                    c.ConversationWallClockMs,
                    c.FailureReason)))
                .ToList(),
            Paired = paired,
            Excluded = excluded,
            Headline = new BenchHeadline(
                paired.Count,
                excluded.Count,
                baselineTokens,
                treatmentTokens,
                saved,
                baselineTokens > 0 ? Math.Round((double)saved / baselineTokens, 6) : 0d,
                SumNullable(paired.Select(p => p.MafCompact.TotalProxyTurnDurationMs)),
                SumNullable(paired.Select(p => p.Comprexy.TotalProxyTurnDurationMs)),
                SumNullableCounts(paired.Select(p => p.Comprexy.ClientCompactionCount)))
        };
    }

    private static long? SumNullable(IEnumerable<long?> values)
    {
        var present = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return present.Count == 0 ? null : present.Sum();
    }

    /// <summary>Null when no paired conversation had client compaction armed at all.</summary>
    private static int? SumNullableCounts(IEnumerable<int?> values)
    {
        var present = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return present.Count == 0 ? null : present.Sum();
    }
}
