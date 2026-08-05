namespace Comprexy.Bench.Running;

/// <summary>
/// Composes the system message every bench conversation sends: a client-shaped operating preamble
/// followed by the script's scenario text.
/// </summary>
/// <remarks>
/// A real coding client spends on the order of 25–27k tokens on system prompt plus tool schemas
/// before the first user turn (target here ≈26k cl100k_base: ~10.5k composed system + ~15–16k tools),
/// and that fixed floor is what drives a session into compaction. A one-sentence bench instruction
/// measures a workload Comprexy will never see, so the preamble and tool descriptions are
/// deliberately sized into that band. The preamble lives in <c>agent-preamble.md</c> next to the
/// scripts so it can be edited without a rebuild, and it is folded into the prompt-list hash so a
/// report refuses to pair conversations whose fixed overhead drifted.
/// </remarks>
internal static class BenchSystemPrompt
{
    public const string PreambleFileName = "agent-preamble.md";

    private const string DefaultScenario = """
        You are a coding agent working inside a small sandboxed project directory.
        Use the provided tools to read, search, edit, and run commands against that directory.
        Prefer reading a file before editing it, and keep changes minimal and consistent with
        the surrounding code. Answer with a short summary of what you found or changed.
        """;

    private static readonly Lazy<string> LazyPreamble = new(LoadPreamble, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string Compose(string? scenario) =>
        string.Concat(
            LazyPreamble.Value.TrimEnd(),
            "\n\n",
            string.IsNullOrWhiteSpace(scenario) ? DefaultScenario : scenario.Trim());

    private static string LoadPreamble()
    {
        var path = Path.Combine(BenchPaths.ConversationsDirectory, PreambleFileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The bench system-prompt preamble is missing: {path}. Every conversation sends it, " +
                "so running without it would silently measure a workload with no fixed context cost.",
                path);
        }

        return File.ReadAllText(path);
    }
}
