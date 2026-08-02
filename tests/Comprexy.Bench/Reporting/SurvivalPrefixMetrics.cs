using System.Text.RegularExpressions;
using Comprexy.Bench.Model;
using Microsoft.Data.Sqlite;

namespace Comprexy.Bench.Reporting;

/// <summary>
/// Reads the bench SQLite file the run used. Prompt boundaries come from plain user rows;
/// turns and compression events before the next prompt's CreatedAt belong to the common prefix.
/// </summary>
internal static class SurvivalPrefixMetrics
{
    private static readonly Regex UserQuery = new(
        @"<user_query>\s*(.*?)\s*</user_query>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled |
        RegexOptions.Singleline);

    public static SurvivalPrefixComparison? TryCompute(
        string databasePath,
        Guid mafCompactConversationId,
        Guid comprexyConversationId,
        int baselinePromptsCompleted)
    {
        if (baselinePromptsCompleted <= 0 ||
            string.IsNullOrWhiteSpace(databasePath) ||
            !File.Exists(databasePath))
        {
            return null;
        }

        try
        {
            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared
            }.ToString();

            using var connection = new SqliteConnection(cs);
            connection.Open();

            var mafId = FormatId(mafCompactConversationId);
            var cxId = FormatId(comprexyConversationId);

            var mafCutoff = TryPromptCutoffTicks(connection, mafId, baselinePromptsCompleted);
            var cxCutoff = TryPromptCutoffTicks(connection, cxId, baselinePromptsCompleted);
            if (mafCutoff is null || cxCutoff is null)
            {
                return null;
            }

            var mafStart = TryFirstPlainUserTicks(connection, mafId);
            var cxStart = TryFirstPlainUserTicks(connection, cxId);
            if (mafStart is null || cxStart is null)
            {
                return null;
            }

            var maf = SumTurns(connection, mafId, mafCutoff.Value);
            var cx = SumTurns(connection, cxId, cxCutoff.Value);
            var overhead = SumCompressionOverhead(connection, cxId, cxCutoff.Value);
            var treatmentCost = cx.TotalTokens + overhead;
            var saved = maf.TotalTokens - treatmentCost;
            var ratio = maf.TotalTokens > 0
                ? Math.Round((double)saved / maf.TotalTokens, 6)
                : 0d;

            return new SurvivalPrefixComparison(
                baselinePromptsCompleted,
                baselinePromptsCompleted + 1,
                maf.TotalTokens,
                cx.TotalTokens,
                overhead,
                treatmentCost,
                saved,
                ratio,
                maf.PeakPromptSent,
                cx.PeakPromptSent,
                maf.TurnCount,
                cx.TurnCount,
                TicksToMilliseconds(mafCutoff.Value - mafStart.Value),
                TicksToMilliseconds(cxCutoff.Value - cxStart.Value),
                maf.ProxyTurnDurationMs,
                cx.ProxyTurnDurationMs);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine(
                $"warning: survival prefix metrics unavailable ({ex.Message}); summary will omit the common-prefix token table.");
            return null;
        }
    }

    private static string FormatId(Guid id) => id.ToString("D").ToUpperInvariant();

    private static long TicksToMilliseconds(long ticks) =>
        ticks <= 0 ? 0 : ticks / TimeSpan.TicksPerMillisecond;

    private static long? TryFirstPlainUserTicks(SqliteConnection connection, string conversationId)
    {
        var users = ListPlainUserCreatedAtTicks(connection, conversationId);
        return users.Count > 0 ? users[0] : null;
    }

    /// <summary>
    /// CreatedAt ticks of the plain user message that starts prompt <paramref name="completedPrompts"/>+1
    /// (1-based), i.e. the first prompt the baseline did not complete. Turns with CreatedAt strictly
    /// before that bound belong to prompts 1..completedPrompts.
    /// </summary>
    private static long? TryPromptCutoffTicks(
        SqliteConnection connection,
        string conversationId,
        int completedPrompts)
    {
        var users = ListPlainUserCreatedAtTicks(connection, conversationId);
        if (users.Count > completedPrompts)
        {
            return users[completedPrompts];
        }

        if (users.Count == completedPrompts)
        {
            return long.MaxValue;
        }

        return null;
    }

    private static List<long> ListPlainUserCreatedAtTicks(SqliteConnection connection, string conversationId)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT CreatedAt, Content
            FROM ConversationMessages
            WHERE ConversationId = $id AND Role = 'User'
            ORDER BY Sequence
            """;
        cmd.Parameters.AddWithValue("$id", conversationId);

        var list = new List<long>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var created = reader.GetInt64(0);
            var content = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            if (IsPlainScriptUser(content))
            {
                list.Add(created);
            }
        }

        return list;
    }

    private static bool IsPlainScriptUser(string content)
    {
        var text = content ?? string.Empty;
        var match = UserQuery.Match(text);
        if (match.Success)
        {
            text = match.Groups[1].Value.Trim();
        }

        if (text.StartsWith("Called the ", StringComparison.Ordinal) ||
            text.Contains("tool with the following input:", StringComparison.Ordinal))
        {
            return false;
        }

        return text.Length > 0;
    }

    private static (int TurnCount, long TotalTokens, long PeakPromptSent, long ProxyTurnDurationMs) SumTurns(
        SqliteConnection connection,
        string conversationId,
        long cutoffExclusiveTicks)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT RawInputTokensEstimated,
                   CompressedInputTokensEstimated,
                   ActualPromptTokens,
                   ActualCompletionTokens,
                   BaselineTotalTokensEstimated,
                   CompressedTotalTokensEstimated,
                   NetTokensSaved,
                   NetTokenSavingsRatio,
                   DurationMs
            FROM ConversationTurnMetrics
            WHERE ConversationId = $id AND CreatedAt < $cutoff
            """;
        cmd.Parameters.AddWithValue("$id", conversationId);
        cmd.Parameters.AddWithValue("$cutoff", cutoffExclusiveTicks);

        var turnCount = 0;
        long totalTokens = 0;
        long peakPromptSent = 0;
        long proxyTurnDurationMs = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            turnCount++;
            var rawInput = reader.GetInt32(0);
            var compressedInputEst = reader.GetInt32(1);
            int? actualPrompt = reader.IsDBNull(2) ? null : reader.GetInt32(2);
            var completion = reader.GetInt32(3);
            var baselineEst = reader.GetInt32(4);
            var compressedTotalEst = reader.GetInt32(5);
            var netSaved = reader.GetInt32(6);
            var netRatio = reader.GetDouble(7);
            if (!reader.IsDBNull(8))
            {
                proxyTurnDurationMs += reader.GetInt32(8);
            }

            var projected = ProjectTurn(
                rawInput,
                compressedInputEst,
                actualPrompt,
                completion,
                baselineEst,
                compressedTotalEst,
                netSaved,
                netRatio);
            totalTokens += projected.CompressedTotal;
            if (projected.CompressedInput > peakPromptSent)
            {
                peakPromptSent = projected.CompressedInput;
            }
        }

        return (turnCount, totalTokens, peakPromptSent, proxyTurnDurationMs);
    }

    /// <summary>
    /// Mirrors <c>PromptTokenBasis.ProviderActual</c> read-side projection (bench reports use
    /// upstream usage when present on both arms).
    /// </summary>
    private static (int CompressedInput, long CompressedTotal) ProjectTurn(
        int rawInputEstimated,
        int compressedInputEstimated,
        int? actualPromptTokens,
        int actualCompletionTokens,
        int baselineTotalEstimated,
        int compressedTotalEstimated,
        int netTokensSaved,
        double netTokenSavingsRatio)
    {
        if (actualPromptTokens is not int actual || actual <= 0)
        {
            return (compressedInputEstimated, compressedTotalEstimated);
        }

        var compressedInput = actual;
        var compressedTotal = compressedInput + actualCompletionTokens;
        _ = (rawInputEstimated, baselineTotalEstimated, netTokensSaved, netTokenSavingsRatio);
        return (compressedInput, compressedTotal);
    }

    private static long SumCompressionOverhead(
        SqliteConnection connection,
        string conversationId,
        long cutoffExclusiveTicks)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT COALESCE(SUM(TotalTokens), 0)
            FROM CompressionEvents
            WHERE ConversationId = $id
              AND Status = 'Succeeded'
              AND CompletedAt IS NOT NULL
              AND CompletedAt < $cutoff
            """;
        cmd.Parameters.AddWithValue("$id", conversationId);
        cmd.Parameters.AddWithValue("$cutoff", cutoffExclusiveTicks);
        var scalar = cmd.ExecuteScalar();
        return scalar is long l ? l : Convert.ToInt64(scalar ?? 0L);
    }
}
