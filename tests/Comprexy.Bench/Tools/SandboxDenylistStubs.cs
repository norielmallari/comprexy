using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Comprexy.Bench.Tools;

/// <summary>
/// Denylist-named stub tools matching stock <c>ToolSchema:ExcludeFromModelTools</c>. Present on
/// both arms' client catalogs; Virtual hides them from the model and rejects local calls with
/// <c>tool_excluded</c>. No filesystem or network I/O.
/// </summary>
internal static class SandboxDenylistStubs
{
    private const string Unavailable =
        "not available in this harness; this tool is a catalog stub matching ExcludeFromModelTools.";

    public static IReadOnlyList<AITool> CreateTools() =>
    [
        AIFunctionFactory.Create(ReadLints),
        AIFunctionFactory.Create(TodoWrite),
        AIFunctionFactory.Create(AwaitShell),
        AIFunctionFactory.Create(UpdateCurrentStep),
        AIFunctionFactory.Create(EditNotebook),
        AIFunctionFactory.Create(SwitchMode),
        AIFunctionFactory.Create(AgentManager, new AIFunctionFactoryOptions { Name = "agent_manager" }),
        AIFunctionFactory.Create(AgentManagerModels, new AIFunctionFactoryOptions { Name = "agent_manager_models" }),
        AIFunctionFactory.Create(BackgroundProcess, new AIFunctionFactoryOptions { Name = "background_process" }),
        AIFunctionFactory.Create(KiloLocalRecall, new AIFunctionFactoryOptions { Name = "kilo_local_recall" })
    ];

    [Description("""
        Read linter and diagnostics output for workspace files, matching the shape of an IDE
        ReadLints / diagnostics tool.

        In a real coding client this surfaces compiler errors, analyzer warnings, and language-server
        diagnostics for one or more paths so you can fix issues without grepping log files. Prefer it
        after edits that might introduce type errors, after renames, and before claiming a change is
        clean.

        When to use it:
        - After a non-trivial edit or write, to confirm the touched files and their neighbors are
          free of new errors.
        - When the user asks whether the project currently builds or type-checks and a full shell
          build is too slow or unavailable.
        - To narrow a failure to a specific path before opening the file with read_file.

        When not to use it:
        - As a substitute for reading the code. Diagnostics name symptoms; you still need the source.
        - To format or auto-fix — this tool only reports.
        - Against paths outside the workspace.

        Output expectations (real client):
        - Per-file groups of severity, code, message, and range (line/character).
        - Empty result means no diagnostics in scope, not that the tool failed.
        - Severities typically include error, warning, information, and hint.

        Path and filter notes:
        - paths may be files or directories; directories expand to known documents the language service
          already has open or recently touched.
        - severities filters the returned set; omit to include all.
        - max_results caps the list so a noisy project does not flood the transcript.

        Harness behavior:
        - Always returns a short fixed stub message. No language service is attached.
        - Does not read or write files. Present so the Off arm pays IDE-comparable catalog tax and
          the Virtual arm can exercise denylist hide / local reject.

        Diagnostics interpretation tips (real client):
        - Fix errors before warnings when both appear; cascading type errors often clear after the
          first missing import or wrong generic argument.
        - A diagnostic on a generated file usually means fixing the generator input, not hand-editing
          the generated output.
        - When the same code appears on many files after a shared signature change, fix the shared
          definition once and re-run diagnostics rather than patching every consumer blindly.
        - Empty results after a large edit can mean the language service has not reloaded yet; a
          second call after a short wait is reasonable in a real IDE. In this harness every call is
          still a stub.
        - Prefer path filters that match what you just touched. Whole-repo diagnostic dumps are hard
          to act on and burn context.
        """)]
    private static string ReadLints(
        [Description("Workspace-relative files or directories to inspect. Empty means the client's default open set.")]
        string[]? paths = null,
        [Description("Optional severity filter: error, warning, information, hint. Omit for all.")]
        string[]? severities = null,
        [Description("Maximum diagnostics to return after filtering. Default 100 in real clients.")]
        int maxResults = 100,
        [Description("When true, include previously suppressed or ignored diagnostics if the client supports that.")]
        bool includeSuppressed = false)
    {
        _ = (paths, severities, maxResults, includeSuppressed);
        return Unavailable;
    }

    [Description("""
        Create or update a structured todo list for the current agent session, matching an IDE
        TodoWrite tool.

        Use todos to track multi-step work the user can see: planned steps, in-progress items, and
        completed checkboxes. Keep the list short and actionable. Update statuses as you go rather
        than rewriting the whole plan every turn unless the plan genuinely changed.

        When to use it:
        - Multi-step implementation or investigation where losing the plan would waste turns.
        - Explicit user requests to track tasks or show a checklist.
        - Handing off state across a long session so later turns know what remains.

        When not to use it:
        - Single-step answers or trivial edits.
        - As a substitute for actually doing the work.
        - Storing large code snippets or secrets in todo text.

        Item rules (real client):
        - Each item needs a stable id, a short content string, and a status among pending,
          in_progress, completed, and cancelled.
        - Prefer merging by id when updating; do not invent duplicate ids for the same work.
        - At most one item should be in_progress unless the client explicitly allows parallel work.

        Harness behavior:
        - Returns a fixed stub message. No UI todo panel is updated.
        - No persistence across turns beyond what the model keeps in context.

        List hygiene:
        - Prefer updating statuses over deleting and recreating items with new ids.
        - Cancel items that are no longer relevant instead of leaving them pending forever.
        - Keep content strings under roughly one line; put detail in the implementation work, not in
          the todo text.
        - When merge is false, you are replacing the entire visible list — include every item that
          should remain, not only the delta.
        - Do not encode secrets, absolute machine paths, or personal data into todo content.
        """)]
    private static string TodoWrite(
        [Description("Full replacement or merge set of todo items for the session.")]
        TodoItem[] todos,
        [Description("When true, merge by id into the existing list; when false, replace the list.")]
        bool merge = true)
    {
        _ = (todos, merge);
        return Unavailable;
    }

    [Description("""
        Wait for a background shell or long-running process to emit output or exit, matching an IDE
        AwaitShell / process-wait tool.

        Use after starting a build, test, or watcher that continues outside the foreground shell tool
        budget. Poll with a timeout rather than busy-looping in the chat.

        When to use it:
        - A prior background_process or shell start returned a handle you must join.
        - You need the next chunk of stdout/stderr without blocking the entire agent turn forever.

        When not to use it:
        - For foreground RunShellCommand calls that already waited to completion.
        - As a general sleep. Prefer event-driven waits tied to a real process id.

        Parameters:
        - shell_id / session_id identify the process to await.
        - pattern is an optional regex matched against new output; return when matched or on timeout.
        - block_until_ms is the maximum wait for this call.

        Harness behavior:
        - Always returns the fixed stub string. No processes are tracked.

        Wait semantics (real client):
        - pattern uses the client's regex dialect; invalid patterns should fail fast rather than hang.
        - block_until_ms is a cap for this call, not a promise the process finishes.
        - Prefer returning on meaningful output chunks over waiting for full process exit when you
          only need to know the server started listening.
        - Do not busy-loop AwaitShell with zero backoff in the parent turn; space waits and do other
          useful work between them when the client allows.
        - If shell_id is unknown, treat that as an error and restart the background work rather than
          inventing an id.
        """)]
    private static string AwaitShell(
        [Description("Identifier of the shell or background session to wait on.")]
        string shellId,
        [Description("Optional regex matched against stdout/stderr. Omit to wait for exit or timeout only.")]
        string? pattern = null,
        [Description("Maximum milliseconds to block for this wait call.")]
        int blockUntilMs = 30_000,
        [Description("When true, return as soon as the process exits even if pattern has not matched.")]
        bool returnOnExit = true)
    {
        _ = (shellId, pattern, blockUntilMs, returnOnExit);
        return Unavailable;
    }

    [Description("""
        Update the user-visible progress step for the current agent turn, matching UpdateCurrentStep
        style IDE telemetry.

        Call when the major subtask changes so the UI can show a short status (for example
        "Reading pricing module" or "Running unit tests"). Keep the text concise and user-facing.
        Set final_summary / completed_subtitle only when finishing the turn's visible work.

        When to use it:
        - Crossing a phase boundary the user would care about.
        - Starting a long tool sequence so the UI is not stuck on a stale label.

        When not to use it:
        - Every tiny tool call. Reserve for meaningful phase changes.
        - Dumping internal chain-of-thought into the step string.

        Harness behavior:
        - Stub only; no parent timeline is updated.

        Step text guidelines:
        - Start with a verb when possible ("Reading", "Editing", "Testing").
        - Avoid internal ids, raw exception stacks, and tool dump fragments in current_step.
        - final_summary should be user-facing and concise when set; it is not a second chain of
          thought.
        - completed_subtitle is a short past-tense label for UI history, not a full report.
        - Do not spam step updates for every tool call; update on meaningful phase changes only.
        """)]
    private static string UpdateCurrentStep(
        [Description("Short present-tense status for the current major step (about six words).")]
        string currentStep,
        [Description("Optional final user-facing summary when the turn's work is complete.")]
        string? finalSummary = null,
        [Description("Optional short past-tense subtitle for the completed turn.")]
        string? completedSubtitle = null)
    {
        _ = (currentStep, finalSummary, completedSubtitle);
        return Unavailable;
    }

    [Description("""
        Edit a Jupyter notebook cell, matching an IDE EditNotebook tool.

        Prefer this over writing raw .ipynb JSON with WriteFile: cell indices, language tags, and
        notebook metadata are easy to corrupt by hand. Provide old_string / new_string for in-cell
        edits, or is_new_cell to insert.

        When to use it:
        - The user asked to change notebook content.
        - You need to add a markdown or code cell at a known index.

        When not to use it:
        - Ordinary source files — use EditFile / WriteFile.
        - Binary notebook attachments or outputs unless the client documents support.

        Harness behavior:
        - Stub only; notebooks on disk are not modified.

        Notebook edit tips (real client):
        - Cell language must match the notebook's expected kernel when inserting code cells.
        - old_string must match the cell body exactly; leading/trailing whitespace mistakes are the
          usual failure mode.
        - Prefer editing one cell per call. Large multi-cell rewrites are hard to review.
        - Do not strip notebook outputs unless the user asked; outputs can be intentional artifacts.
        - is_new_cell inserts at cell_idx and shifts later cells; confirm the index from a prior read
          of the notebook structure when available.
        """)]
    private static string EditNotebook(
        [Description("Workspace-relative path to the .ipynb file.")]
        string targetNotebook,
        [Description("0-based cell index to edit or insert at.")]
        int cellIdx,
        [Description("When true, insert a new cell at cell_idx instead of editing.")]
        bool isNewCell,
        [Description("Cell language: python, markdown, javascript, typescript, r, sql, shell, raw, or other.")]
        string cellLanguage,
        [Description("Exact prior cell text to replace when editing an existing cell.")]
        string oldString,
        [Description("Replacement or new cell content.")]
        string newString)
    {
        _ = (targetNotebook, cellIdx, isNewCell, cellLanguage, oldString, newString);
        return Unavailable;
    }

    [Description("""
        Request a switch of the interactive agent mode (plan vs agent, ask, debug, and similar),
        matching an IDE SwitchMode tool.

        Use when the work clearly needs a different collaboration mode — for example moving from
        implementation into planning when architecture trade-offs appear. The user must approve mode
        switches in real clients.

        When to use it:
        - The task type changed and staying in the current mode would fight the UI/tooling.
        - You would otherwise ask several clarifying questions better handled in plan mode.

        When not to use it:
        - Mid-implementation when progress is fine.
        - As a substitute for asking a single clarifying question.

        Harness behavior:
        - Stub only; mode does not change.

        Mode switch etiquette:
        - Explain in one short sentence why the current mode is a poor fit.
        - Do not thrash between modes; wait for user approval before assuming the switch landed.
        - Prefer staying put for minor clarifying questions.
        - target_mode_id must be a mode the client actually exposes; inventing ids fails closed.
        """)]
    private static string SwitchMode(
        [Description("Target mode id, for example plan or agent.")]
        string targetModeId,
        [Description("Optional short explanation shown to the user for approval.")]
        string? explanation = null)
    {
        _ = (targetModeId, explanation);
        return Unavailable;
    }

    [Description("""
        Manage background or sibling agents in a multi-agent IDE session (list, stop, focus), matching
        Kilo-style agent_manager tooling.

        Use to inspect running agents, cancel runaway work, or focus the UI on a particular agent id.
        This is orchestration metadata, not a substitute for Task when you need a subagent to do
        repository work.

        When to use it:
        - The user asks which agents are running or wants one stopped.
        - You need to attach follow-up instructions to an existing agent handle the client exposed.

        When not to use it:
        - Starting a fresh investigation — prefer Task for work, not manager APIs.
        - Ordinary single-agent coding sessions with nothing to manage.

        Harness behavior:
        - Stub only; no agent roster exists in this harness.

        Manager actions:
        - list returns running/pending agents in a real client.
        - stop cancels work; confirm with the user before stopping an agent that may hold edits.
        - focus brings an agent's transcript to the foreground UI.
        - status is a lightweight poll without changing focus.
        - Unknown agent_id should error clearly rather than no-op silently.
        """)]
    private static string AgentManager(
        [Description("Action to perform: list, stop, focus, or status.")]
        string action,
        [Description("Optional agent id for stop/focus/status.")]
        string? agentId = null,
        [Description("Optional free-form reason recorded in client telemetry.")]
        string? reason = null)
    {
        _ = (action, agentId, reason);
        return Unavailable;
    }

    [Description("""
        List or select models available to managed agents, matching agent_manager_models.

        Use when the client exposes per-agent model overrides and the user asks which models can be
        assigned. Does not itself run inference.

        Harness behavior:
        - Stub only; returns the unavailable message.

        Model listing notes:
        - include_experimental may surface preview models that are slower or less reliable.
        - Results are advisory; assigning a model is a separate client action.
        - Do not treat the returned list as permission to call paid providers the user did not enable.
        - When agent_id is set, a real client may return only models allowed for that agent role
          (explore vs implement). When omitted, expect the broader workspace-default catalog.
        - Prefer the user's configured default model unless they explicitly asked to compare options.
        - Do not call this tool on every turn; fetch models when the user asks or when assigning a
          managed agent that needs an explicit override.
        """)]
    private static string AgentManagerModels(
        [Description("Optional agent id whose model options to list; omit for the global catalog.")]
        string? agentId = null,
        [Description("When true, include disabled or experimental model entries if the client supports that.")]
        bool includeExperimental = false)
    {
        _ = (agentId, includeExperimental);
        return Unavailable;
    }

    [Description("""
        Start, inspect, or stop a background OS process owned by the IDE session, matching
        background_process tooling.

        Prefer this for long-lived watchers and servers that should outlive a single shell tool call.
        Pair with AwaitShell (or equivalent) to collect output.

        When to use it:
        - Dev servers, file watchers, or long test runners the user wants kept alive.
        - Reattaching to a process id the client previously returned.

        When not to use it:
        - Short commands — use RunShellCommand.
        - Destructive system changes without explicit user request.

        Harness behavior:
        - Stub only; no process is started or signaled.

        Process lifecycle:
        - start returns a process_id in a real client; keep it for status/stop/AwaitShell.
        - status should report running vs exited and a short tail of output when available.
        - stop should be graceful first when the client supports signals; escalate only if needed.
        - list enumerates processes owned by this session, not the whole OS.
        - Do not start interactive REPLs that wait for stdin.
        """)]
    private static string BackgroundProcess(
        [Description("Action: start, status, stop, or list.")]
        string action,
        [Description("Command line for start. Ignored for other actions.")]
        string? command = null,
        [Description("Process or session id for status/stop.")]
        string? processId = null,
        [Description("Optional working directory relative to the workspace for start.")]
        string? workingDirectory = null,
        [Description("Optional environment overrides as KEY=VALUE pairs.")]
        string[]? environment = null)
    {
        _ = (action, command, processId, workingDirectory, environment);
        return Unavailable;
    }

    [Description("""
        Recall a snippet from the client's local memory / recall store (Kilo kilo_local_recall style).

        Use when the user previously saved a preference, path, or note into local recall and you need
        it again without searching the repository. Not a substitute for reading project files.

        When to use it:
        - User refers to something they asked you to remember earlier in another session.
        - Looking up a stored credential handle name (never the secret itself) the client manages.

        When not to use it:
        - Ordinary codebase questions — use SearchFiles / ReadFile.
        - As a general web search.

        Harness behavior:
        - Stub only; no recall database is queried.

        Recall usage:
        - Prefer exact keys when you know them; otherwise a short natural-language query.
        - limit keeps the transcript small; raise it only when the first page is insufficient.
        - namespace partitions personal vs project recall in clients that support it.
        - Never echo recalled secrets into the chat; reference handles only.
        - If recall returns nothing, fall back to repository search rather than inventing a memory.
        - Prefer project files over recall when the question is about code that exists in the tree.
        """)]
    private static string KiloLocalRecall(
        [Description("Query string or recall key to look up.")]
        string query,
        [Description("Maximum number of recall hits to return.")]
        int limit = 5,
        [Description("Optional namespace or collection hint if the client partitions recall.")]
        string? @namespace = null)
    {
        _ = (query, limit, @namespace);
        return Unavailable;
    }

    internal sealed record TodoItem(
        [property: Description("Stable todo id used for merge updates.")] string id,
        [property: Description("Short actionable todo text.")] string content,
        [property: Description("Status: pending, in_progress, completed, or cancelled.")] string status);
}
