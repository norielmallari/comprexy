namespace Comprexy.Bench.Hosting;

/// <summary>
/// One side of the comparison. Arm behaviour is expressed only as process environment on the
/// spawned proxy — the harness never rewrites repo configuration files.
/// </summary>
internal sealed record BenchArm(
    string Name,
    string Description,
    int Port,
    bool UsesClientCompaction,
    IReadOnlyDictionary<string, string> Environment)
{
    public const string MafCompact = "maf-compact";
    public const string Comprexy = "comprexy";

    public string BaseUrl => $"http://127.0.0.1:{Port}";

    /// <summary>
    /// Baseline: Comprexy compression cannot fire (soft limit set unreachable) and the model sees
    /// the client's own tool catalog. MAF client compaction is the only compression in play.
    /// </summary>
    public static BenchArm CreateMafCompact(int port) => new(
        MafCompact,
        "Baseline — MAF client compaction alone (ToolSchema off, soft limit unreachable)",
        port,
        UsesClientCompaction: true,
        new Dictionary<string, string>
        {
            ["ToolSchema__Mode"] = "Off",
            ["ContextPolicy__SoftLimitTokens"] = "100000000"
        });

    /// <summary>
    /// Treatment: Virtual Tools plus the operator's normal soft limit from the host config chain.
    /// The soft limit is deliberately not overridden so the run reflects a stock deployment.
    /// MAF client compaction is off here: it is the baseline's treatment, and leaving it armed makes
    /// the arm measure two compressors at once, with no way to attribute a result to either.
    /// </summary>
    public static BenchArm CreateComprexy(int port) => new(
        Comprexy,
        "Treatment — Comprexy compression + Virtual Tools at the configured soft limit (no client compaction)",
        port,
        UsesClientCompaction: false,
        new Dictionary<string, string>
        {
            ["ToolSchema__Mode"] = "Virtual"
        });
}
