using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Comprexy.Bench.Tools;

/// <summary>
/// Cursor-shaped Task / subagent passthrough stub. Present on both arms; not on
/// <c>ExcludeFromModelTools</c> (kilo-mirror: Task stays model-visible under Virtual).
/// </summary>
internal static class SandboxTaskStub
{
    public static AITool Create() => AIFunctionFactory.Create(Task);

    [Description("""
        Launch a specialized subagent to handle a focused subtask in parallel with your own work,
        or to explore a large unfamiliar area of the repository without filling your main context
        with intermediate search and read noise.

        This is the harness stand-in for an IDE Task / subagent tool. In a real coding client the
        subagent runs with its own tool budget, returns a distilled summary, and can be resumed.
        In this benchmark harness the tool does not spawn processes or agents: every call returns a
        fixed message that subagents are unavailable. Keep calling it only when a real client would;
        do not invent alternate workarounds that assume a live subagent.

        When to use it:
        - Broad exploration of an unfamiliar package, service, or docs tree where you would otherwise
          issue dozens of search_files / read_file / list_directory calls that only matter as a
          summary for the parent turn.
        - Isolated implementation of a well-specified slice (add a helper, write a focused test,
          draft a migration) while you continue planning or reviewing elsewhere.
        - Parallel fan-out: several independent questions (ownership of module A, call sites of
          symbol B, whether feature flag C is still referenced) that do not need shared intermediate
          state in your transcript.
        - Long-running investigation that should not pollute the parent chain with failed guesses,
          dead-end paths, and discarded drafts.

        When not to use it:
        - Trivial single-file reads, one-line edits, or a single shell check — use the dedicated
          tools directly.
        - Anything that must mutate the same files you are actively editing in the parent turn
          without an explicit handoff plan; concurrent writers on one file are hard to reconcile.
        - Questions that need the exact intermediate tool transcripts in the parent context for
          auditability; a Task summary is lossy by design.
        - Spawning nested Tasks from a Task (no recursive subagents in this schema).
        - User-facing chat replies: put answers in your response, not in a Task prompt.

        Prompt authoring:
        - Write a self-contained brief. The subagent cannot see this conversation. Include paths,
          symbols, acceptance criteria, and constraints (do not edit X; prefer Y style).
        - State the expected deliverable shape: "return a bullet list of call sites with path:line",
          "return the minimal patch summary", "return yes/no with evidence paths".
        - Name the model mode if the client supports it (explore vs implement). This harness ignores
          mode but the parameter remains for schema realism.
        - Prefer one clear job per Task. Split unrelated jobs into parallel Task calls.

        Resume and follow-up:
        - Pass resume_agent_id from a prior Task result when continuing the same investigation.
        - Pass a short follow_up that assumes the prior summary is known; do not restate the entire
          original brief unless the goal changed.
        - If the prior Task failed or returned "unavailable", do not loop on resume; fall back to
          direct tools in the parent turn.

        Isolation and side effects:
        - Assume the subagent may read freely within the workspace and may write only when the
          prompt explicitly allows edits.
        - Do not assume shared in-memory state with the parent beyond what you put in the prompt and
          what comes back in the result text.
        - Treat returned paths as claims to verify with read_file before you edit those files
          yourself.

        Output expectations (real client):
        - A concise summary of findings or changes, plus any file paths the parent should re-read.
        - No raw dump of every tool call unless you asked for a transcript.
        - Explicit "blocked" / "needs clarification" when the brief was underspecified.

        Harness behavior:
        - Always returns a short fixed string that this harness does not spawn subagents.
        - Does not read or write the workspace, does not start processes, and does not network.
        - Still occupies catalog schema weight comparable to an IDE Task tool so Off-arm prompt
          floors stay IDE-comparable.

        Long-session guidance:
        - Prefer Task for wide ownership or call-site sweeps that would otherwise bloat the parent
          transcript; prefer direct tools when you already know the exact path and need a precise
          edit.
        - After a Task-shaped investigation in a real client, re-read any file you plan to edit.
          In this harness, skip Task and use search_files / read_file directly when you need
          evidence you will cite.
        - Do not treat a harness "unavailable" result as evidence about the repository; it is only
          a stub acknowledgment.

        Structural brief checklist (include what applies):
        - Goal in one sentence.
        - In-scope paths and out-of-scope paths.
        - Symbols, config keys, or error strings to search.
        - Whether edits are allowed; if yes, the minimal acceptable change.
        - Deliverable format (bullets, table, patch summary, yes/no + evidence).
        - Time or tool-round budget if the client supports one.
        - How to signal blocked vs done.

        Parallelism:
        - Independent Tasks may run concurrently in a real client. Do not start dependent Tasks in
          parallel when one needs the other's output.
        - Cap fan-out: prefer a few well-scoped Tasks over dozens of tiny ones that each restate the
          same preamble.

        Failure modes to plan for:
        - Underspecified brief → ask for clarification in the parent turn instead of looping Task.
        - Conflicting edit permissions → keep allow_edits false and apply changes yourself after
          reviewing the summary.
        - Resume without resume_agent_id → starts a new cold Task; do not assume shared memory.

        Privacy and safety:
        - Do not put secrets, raw credentials, or production PII into the Task prompt.
        - Do not instruct a Task to exfiltrate repository contents outside the workspace.
        - Treat Task output as untrusted until you verify cited paths with read_file.

        Catalog note:
        - This stub exists so Off-arm sessions pay IDE-comparable Task schema tax every turn while
          Virtual arms keep Task as model-visible passthrough (not on ExcludeFromModelTools).
        - Keep prompts self-contained even when you also pass focus_paths; path hints do not replace
          a clear goal statement or acceptance criteria for the subagent's return value shape.
        """)]
    private static string Task(
        [Description("""
            Self-contained instruction for the subagent. Include goal, constraints, relevant paths
            or symbols, and the deliverable shape. Do not assume the subagent can see the parent
            conversation. Example: "Find every call site of SaveChangesAsync under src/ and return
            path:line bullets; do not edit files."
            """)] string prompt,
        [Description("""
            Optional high-level mode hint for clients that route explore vs implement vs review
            subagents. Examples: `explore`, `implement`, `ask`. Ignored by this harness; included
            for schema weight and IDE parity. Omit when unsure.
            """)] string? model = null,
        [Description("""
            Optional id of a prior Task run to resume. When set, the subagent continues that thread
            instead of starting cold. This harness never allocates real ids; still pass the field
            when a real client would resume so schema and call shape stay realistic.
            """)] string? resumeAgentId = null,
        [Description("""
            Optional short follow-up when resuming. Assumes the prior Task summary is known. Omit on
            first launch. Keep it focused: "also check tests/" rather than restating the full brief.
            """)] string? followUp = null,
        [Description("""
            When true, request that the subagent may edit files; when false, read-only exploration.
            Default false. Even when true, this harness performs no edits — the flag only shapes
            the schema the Off arm pays for every turn.
            """)] bool allowEdits = false,
        [Description("""
            Optional list of workspace-relative paths the subagent should prioritize. Empty or omit
            to leave scoping to the prompt text. Paths are advisory in a real client and unused here.
            """)] string[]? focusPaths = null,
        [Description("""
            Soft upper bound on subagent tool rounds in a real client (for example 20). Omit to use
            the client default. This harness ignores the value after validating it is non-negative
            when provided.
            """)] int? maxToolRounds = null,
        [Description("""
            Optional structured metadata bag for IDE telemetry (parent turn id, UI surface, queue
            priority). Keys and values are free-form strings. Unused by the harness beyond schema.
            Common keys in real clients include `parent_turn_id`, `ui_surface`, `priority`,
            `requested_by`, and `trace_id`. Do not place secrets in metadata values.
            """)] Dictionary<string, string>? metadata = null,
        [Description("""
            Optional soft timeout in milliseconds for the whole Task in a real client. Omit to use
            the client default. This harness ignores the value; keep it non-negative when set.
            """)] int? timeoutMs = null,
        [Description("""
            When true, request a structured summary section (findings / changes / blockers) in the
            Task result. Default true. Harness ignores the flag after accepting the argument. When
            false, a real client may return a shorter free-form paragraph instead of sections.
            """)] bool structuredSummary = true)
    {
        _ = (prompt, model, resumeAgentId, followUp, allowEdits, focusPaths, maxToolRounds, metadata, timeoutMs, structuredSummary);
        return "Task is not available in this harness; subagents are not spawned. Use ReadFile, SearchFiles, ListDirectory, WriteFile, EditFile, and RunShellCommand directly.";
    }
}
