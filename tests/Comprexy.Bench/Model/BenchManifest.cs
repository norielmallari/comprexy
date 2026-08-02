namespace Comprexy.Bench.Model;

/// <summary>Terminal state of one conversation run. Only <c>completed</c> pairs into token headlines.</summary>
internal static class ConversationStatus
{
    public const string Completed = "completed";

    /// <summary>The conversation exceeded its wall-clock cap across all prompts.</summary>
    public const string TimedOut = "timed_out";

    /// <summary>
    /// One completion exceeded the per-call cap. Distinct from <see cref="TimedOut"/> because it
    /// names a provider that stalled on a single prompt rather than a conversation that ran long —
    /// the expected outcome when a prompt outgrows what the upstream model can serve.
    /// </summary>
    public const string CompletionStalled = "completion_stalled";

    public const string Failed = "failed";

    /// <summary>
    /// Treatment stopped after clearing the baseline kill zone (default harness behavior). Not a
    /// paired full-script token result; survival past the baseline failure is the result.
    /// </summary>
    public const string SurvivedBaselineFailure = "survived_baseline_failure";

    public static bool IsSuccessfulTerminal(string status) =>
        status is Completed or SurvivedBaselineFailure;
}

/// <summary>
/// Provenance for one run. Written by <c>bench run</c> and consumed by <c>bench report</c>; it is
/// the only place the harness records what was configured, since nothing bench-specific is stored
/// in the Comprexy schema.
/// </summary>
internal sealed record BenchManifest
{
    public required string RunId { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }

    public required string ComprexyCommit { get; init; }

    public required bool RepositoryDirty { get; init; }

    public required string MafPackageVersion { get; init; }

    public required string DatabasePath { get; init; }

    public required string ControlApiBaseUrl { get; init; }

    public required string? Model { get; init; }

    public required BenchHarnessSettings Harness { get; init; }

    public required IReadOnlyList<BenchArmManifest> Arms { get; init; }
}

internal sealed record BenchHarnessSettings(
    int MaxContextWindowTokens,
    int MaxOutputTokens,
    int CompletionTimeoutMs,
    int ConversationTimeoutMs,
    int ShellTimeoutMs,
    int? Seed,
    double Temperature);

internal sealed record BenchArmManifest(
    string Name,
    string Description,
    string BaseUrl,
    bool ClientCompactionEnabled,
    IReadOnlyDictionary<string, string> EnvironmentOverrides,
    ResolvedArmSettings Resolved,
    long ArmWallClockMs,
    IReadOnlyList<BenchConversationRun> Conversations);

/// <summary>
/// What the arm's proxy actually loaded, not what the harness asked for. The treatment arm takes
/// its soft limit from the host config chain, so the resolved value is the only honest record.
/// </summary>
internal sealed record ResolvedArmSettings(
    string ToolSchemaMode,
    int SoftLimitTokens,
    bool PassThrough,
    string? ProviderBaseUrl,
    string? ProviderModel);

/// <summary>
/// <paramref name="ConversationKey"/> is the identity the harness sent on
/// <c>X-Comprexy-Conversation-Id</c>; <paramref name="ConversationId"/> is the entity id Comprexy
/// echoed back and keys its metrics by. Reporting joins on the latter.
/// <paramref name="ClientCompactionCount"/> is null when MAF client compaction was not armed for the
/// arm at all, which is a different fact from an armed strategy that never fired.
/// </summary>
internal sealed record BenchConversationRun(
    string Name,
    Guid ConversationKey,
    Guid? ConversationId,
    string PromptListHash,
    int PromptCount,
    int PromptsCompleted,
    string Status,
    long ConversationWallClockMs,
    int? ClientCompactionCount,
    string? FailureReason);
