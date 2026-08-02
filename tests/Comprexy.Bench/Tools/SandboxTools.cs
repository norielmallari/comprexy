using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.AI;

namespace Comprexy.Bench.Tools;

/// <summary>
/// File and shell tools handed to the bench agent. These are the client-side catalog Comprexy
/// sees on the wire, so their names and schemas stand in for a real coding client's tools.
/// </summary>
internal sealed class SandboxTools(SandboxWorkspace workspace, TimeSpan shellTimeout)
{
    private const int MaxReadCharacters = 60_000;
    private const int MaxShellOutputCharacters = 20_000;

    public IList<AITool> CreateTools() =>
    [
        AIFunctionFactory.Create(ReadFile),
        AIFunctionFactory.Create(WriteFile),
        AIFunctionFactory.Create(EditFile),
        AIFunctionFactory.Create(ListDirectory),
        AIFunctionFactory.Create(SearchFiles),
        AIFunctionFactory.Create(RunShellCommand)
    ];

    [Description("""
        Read a UTF-8 text file from the workspace and return its contents with 1-based line numbers.

        This is the primary way to look at a file, and it should be the first thing you reach for
        before answering a question about code or editing anything. Prefer it over running `cat`,
        `head`, `sed`, or `awk` through the shell: the output is line-numbered, the size is bounded,
        and failures come back as readable messages instead of shell noise.

        Output format:
        Each line is returned as the 1-based line number, a pipe character, and then the original
        line, for example `42|    return total`. The `42|` prefix is metadata added by this tool. It
        is not part of the file, and you must not include it in text you pass to write_file or in the
        old_string of an edit_file call.

        Usage notes:
        - Read the whole file when it is small. Omit start_line and end_line to do that. A partial
          read of a short file is how you miss the import, the constant, or the early return that
          explains the behavior you are investigating.
        - Use start_line and end_line only for files large enough that reading them whole is
          wasteful, and widen the range if the answer is not clearly contained in what came back.
        - Reading a file you have already read in this session is cheap and is the correct thing to
          do after you have written to it, after an edit failed, or whenever you are not certain the
          contents still match what you remember.
        - It is safe to call this tool for a file that may not exist; a missing file returns an error
          string rather than throwing, so you can use it to probe.
        - Call this tool several times in parallel when you need to read several files and none of
          them determines which file you read next.

        Limits and errors:
        - Output is truncated at 60,000 characters, with a trailing line stating that truncation
          happened and how many lines the file has in total. If you hit that, re-read the region you
          care about with an explicit range rather than assuming you saw everything.
        - A path that does not exist returns `error: file not found: <path>`.
        - A start_line past the end of the file returns an error naming the actual line count.
        - Paths must be workspace-relative. Absolute paths and any path that escapes the workspace
          root through `..` are rejected; nothing outside the workspace is readable.
        - The file is decoded as UTF-8 text. Binary files will come back as garbage rather than an
          error, so do not use this tool to inspect one.

        Long-session guidance:
        - When the user asks about a document or source file as a whole, omit start_line and end_line
          and read it in full. Partial reads are for known hotspots in large files, not for avoiding
          work on a file whose structure you have not yet seen.
        - After you write or edit a file, read it back before you claim the change is correct. After
          many turns, re-read files your answer depends on rather than trusting earlier turns.
        - Prefer several parallel read_file calls over a shell pipeline that concatenates files: each
          result stays bounded and line-numbered.
        """)]
    private string ReadFile(
        [Description("""
            Workspace-relative path to the file, for example `src/pricing.py` or `docs/notes.md`.
            Always relative to the workspace root, never absolute, and never containing `..`. Use
            forward slashes. If you do not know the exact path, list the directory or search for a
            distinctive string in the file first rather than guessing at a plausible name. For
            session notes under `scratch/`, pass the same relative path you used when writing them.
            """)] string path,
        [Description("""
            Optional 1-based number of the first line to return. Omit it to start at the beginning of
            the file, which is what you should do unless the file is genuinely too large to read
            whole. Values below 1 are treated as 1. If this is past the end of the file the call
            returns an error naming the real line count rather than empty output.
            """)] int? startLine = null,
        [Description("""
            Optional 1-based number of the last line to return, inclusive. Omit it to read to the end
            of the file. A value past the end of the file is clamped to the last line, so it is safe
            to ask for a range that overshoots. Ignored if it is less than start_line, which yields
            no lines.
            """)] int? endLine = null)
    {
        return Guard(() =>
        {
            var full = workspace.ResolvePath(path);
            if (!File.Exists(full))
            {
                return $"error: file not found: {path}";
            }

            var lines = File.ReadAllLines(full);
            var from = Math.Max(1, startLine ?? 1);
            var to = Math.Min(lines.Length, endLine ?? lines.Length);
            if (from > lines.Length)
            {
                return $"error: {path} has {lines.Length} lines; start_line {from} is past the end.";
            }

            var builder = new StringBuilder();
            for (var i = from; i <= to; i++)
            {
                builder.Append(i).Append('|').AppendLine(lines[i - 1]);
                if (builder.Length > MaxReadCharacters)
                {
                    builder.AppendLine($"… truncated at {MaxReadCharacters} characters ({lines.Length} lines total).");
                    break;
                }
            }

            return builder.ToString();
        });
    }

    [Description("""
        Create a new UTF-8 text file in the workspace, or completely replace the contents of an
        existing one.

        This tool always writes the whole file. There is no append mode and no partial write: the
        content you supply becomes the entire file, and anything previously there is gone. Parent
        directories are created automatically, so you can write to a path several levels deep without
        creating the directories first.

        When to use it:
        - Creating a file that does not exist yet, such as a new module, a new test, a scratch
          verification script, or a document you were asked to write.
        - Replacing a file whose contents you are deliberately regenerating in full.

        When not to use it:
        - Do not use it to change part of an existing file. Use edit_file instead. A whole-file
          rewrite to change three lines produces a diff nobody can review, and it silently discards
          anything in the file you did not reproduce exactly, including code you never read.
        - Do not use it on a file you have not read in this session, unless you are certain the file
          does not exist. Overwriting a file whose current contents you have not seen is destructive
          and unrecoverable here; there is no version control in this environment to fall back on.
        - Do not use it to create documentation, README files, or summary notes that the user did not
          ask for. Unrequested files are clutter the user has to clean up.

        Usage notes:
        - Write the file exactly as it should appear on disk. Do not include line-number prefixes
          like `12|` from read_file output, and do not wrap the content in markdown code fences.
        - Match the conventions of the surrounding project: the same indentation style, the same line
          endings, the same import ordering, the same header or licence block if other files have
          one.
        - Keep scratch and verification scripts in an obviously temporary location so a human can
          tell at a glance that they are not part of the product.
        - After writing something non-trivial, read it back or run the check that would catch a
          mistake. A successful write means the bytes you supplied were stored, not that they were
          the bytes you meant.

        Returns a confirmation naming the number of characters written and the workspace-relative
        path. Paths must be workspace-relative; absolute paths and `..` traversal are rejected.

        Long-session guidance:
        - Scratch notes and walkthroughs under `scratch/` are durable session state. When the user
          asks you to extend one, read the current file, then write the full updated contents
          (write_file replaces the whole file) or use edit_file to append — do not replace a long
          note with a shorter paraphrase unless asked to condense.
        - Put enough path, method, and condition detail into the file that a later turn can audit
          claims without re-deriving them from memory.
        """)]
    private string WriteFile(
        [Description("""
            Workspace-relative path of the file to create or overwrite, for example
            `scratch/verify_pricing.py`. Never absolute, never containing `..`, forward slashes only.
            Missing parent directories are created for you. If a file already exists at this path its
            entire contents will be replaced without any further confirmation.
            """)] string path,
        [Description("""
            The complete contents of the file as it should exist on disk after the call. This is not
            a patch and not an addition: whatever you pass here is the whole file. Include every line
            the file needs, including imports, closing braces, and a trailing newline if the project
            uses them. Do not include line-number prefixes or markdown code fences.
            """)] string content)
    {
        return Guard(() =>
        {
            var full = workspace.ResolvePath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
            return $"wrote {content.Length} characters to {workspace.ToRelative(full)}";
        });
    }

    [Description("""
        Replace one exact fragment of text in an existing workspace file with another.

        This is the preferred way to change a file that already exists. It produces a minimal,
        reviewable diff, it leaves the rest of the file untouched, and it fails loudly instead of
        guessing when the file is not what you expected.

        How the match works:
        - old_string must match the file byte for byte, including indentation, internal whitespace,
          and line breaks. The comparison is ordinal and case-sensitive; there is no fuzzy matching,
          no regular expressions, and no whitespace normalisation.
        - Only the first occurrence in the file is replaced. If the text you want to change appears
          more than once, extend old_string with surrounding context until the fragment you are
          targeting is the first and only place it can match, or make several calls, checking the
          result between them.
        - If old_string is not found the file is left completely unmodified and the call returns
          `error: old_string not found in <path>; the file was not modified.` Treat that as a signal
          that your picture of the file is stale, not as a reason to retry with a guess: read the
          file again and copy the real text.

        Requirements before calling:
        - You must have read the file in this session. Editing from memory of a similar file, or from
          a read you took before your own earlier writes, is the most common cause of a failed or
          misplaced edit.
        - Strip the `12|` line-number prefixes that read_file adds. They are display metadata and are
          not present in the file, so any old_string containing them will never match.

        Usage notes:
        - Include enough context to be unambiguous, but no more than you need. A whole function body
          as old_string when three lines would do makes the change harder to review.
        - Keep the replacement consistent with the surrounding code: same indentation level, same
          naming style, same error-handling idiom. An edit that is obviously machine-generated stands
          out badly in review.
        - To delete text, pass an empty new_string. To insert text, include an existing anchor line
          in both old_string and new_string so the anchor is preserved.
        - Do not use this tool to reformat code you are not otherwise changing; unrelated whitespace
          churn hides the real change.
        - After a non-trivial edit, re-read the region or run the project's checks. An edit that
          succeeded is not necessarily an edit that was correct.

        Returns a short confirmation naming the file that was edited. Paths must be
        workspace-relative; absolute paths and `..` traversal are rejected, and a path that does not
        exist returns an error rather than creating the file.

        Long-session guidance:
        - Prefer edit_file when extending an existing scratch note or correcting a few lines of a
          source file you have just read. Include a unique anchor in old_string so the match cannot
          land on an earlier duplicate section of a growing file.
        - After editing session notes, a quick read_file of the changed region confirms the append
          landed where you intended before you build the next answer on it.
        """)]
    private string EditFile(
        [Description("""
            Workspace-relative path of the file to modify, for example `src/ledger.py`. The file must
            already exist; this tool never creates one. Never absolute, never containing `..`.
            """)] string path,
        [Description("""
            The exact text to find and replace, copied verbatim from the file as it currently is on
            disk. Must match byte for byte including leading indentation and newlines, must not
            include read_file's `12|` line-number prefixes, and must appear in the file at least
            once. Only its first occurrence is replaced, so include enough surrounding context to
            make the intended location unambiguous.
            """)] string oldString,
        [Description("""
            The text that replaces old_string. Pass an empty string to delete the matched text. Mind
            the indentation: what you supply is inserted exactly as given, so the first line needs
            whatever leading whitespace the original had. Include the surrounding anchor lines here
            too if you included them in old_string, otherwise they will be removed.
            """)] string newString)
    {
        return Guard(() =>
        {
            var full = workspace.ResolvePath(path);
            if (!File.Exists(full))
            {
                return $"error: file not found: {path}";
            }

            var content = File.ReadAllText(full);
            var index = content.IndexOf(oldString, StringComparison.Ordinal);
            if (index < 0)
            {
                return $"error: old_string not found in {path}; the file was not modified.";
            }

            File.WriteAllText(full, string.Concat(content[..index], newString, content[(index + oldString.Length)..]));
            return $"edited {workspace.ToRelative(full)}";
        });
    }

    [Description("""
        List the files and directories under a workspace directory.

        Use this to orient yourself in an unfamiliar project before you start reading, to confirm
        that a path exists before you write to it, or to check what a change of yours produced. A
        recursive listing of the workspace root is usually the cheapest possible first move in a new
        session.

        Output format:
        One workspace-relative path per line, sorted ordinally, files and directories mixed together.
        Directories are not marked; if you need to know whether an entry is a directory, list it.
        An empty directory returns a single line saying so.

        Usage notes:
        - Pass `.` to list the workspace root. This is the default, so calling the tool with no
          arguments gives you the top level of the project.
        - Prefer a recursive listing of a small project over several shallow ones. Prefer shallow
          listings when the tree is large and you only need one level.
        - Reach for search_files instead when you are looking for a file by its contents rather than
          by its name or location.
        - Prefer this tool over `ls` or `find` through the shell: the output is already
          workspace-relative and bounded, and a missing directory returns a clear error instead of a
          shell exit code you have to interpret.
        - This is a read-only operation; it never modifies the workspace.

        Limits and errors:
        - At most 500 entries are returned, taken after ordinal sorting. A truncated listing looks
          exactly like a complete one, so if you get 500 entries assume there are more and narrow the
          path or turn off recursion.
        - A path that does not exist, or that names a file rather than a directory, returns
          `error: directory not found: <path>`.
        - Hidden files and directories are included in the listing.
        - Paths must be workspace-relative; absolute paths and `..` traversal are rejected.

        Long-session guidance:
        - Re-list a directory after you create or move files if a later step depends on what is
          present. Do not assume an earlier listing still matches the tree.
        - When exploring an unfamiliar area, one recursive listing of a focused subtree beats many
          shallow guesses across the repository.
        """)]
    private string ListDirectory(
        [Description("""
            Workspace-relative directory to list, for example `src` or `docs/design`. Use `.` for the
            workspace root, which is the default. Never absolute, never containing `..`. Must name an
            existing directory; passing a file path returns an error.
            """)] string path = ".",
        [Description("""
            When true, list everything underneath the directory at any depth; when false, list only
            the immediate children. Default is false. Recursive listings of a large tree will hit the
            500-entry cap, so prefer a narrower starting path over recursing from the root in a big
            project.
            """)] bool recursive = false)
    {
        return Guard(() =>
        {
            var full = workspace.ResolvePath(path);
            if (!Directory.Exists(full))
            {
                return $"error: directory not found: {path}";
            }

            var search = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var entries = Directory.EnumerateFileSystemEntries(full, "*", search)
                .Select(workspace.ToRelative)
                .Order(StringComparer.Ordinal)
                .Take(500)
                .ToList();

            return entries.Count == 0
                ? $"{path} is empty"
                : string.Join(System.Environment.NewLine, entries);
        });
    }

    [Description("""
        Search the text of workspace files for a literal string and return every matching line.

        This is how you find where something is defined, where it is used, and whether it exists at
        all. Search before you assume a symbol is unused, before you write a helper that may already
        exist, and before you change a function without knowing its callers. It is far cheaper to
        search and find nothing than to guess and be wrong.

        Matching behavior:
        - The query is matched as a literal substring, not a regular expression and not a glob.
          Characters like `.`, `*`, `(`, and `[` match themselves.
        - Matching is case-sensitive and ordinal. Searching for `readfile` will not find `ReadFile`.
          If you are unsure of the casing, search for a distinctive case-insensitive fragment of the
          name, or run the search more than once.
        - The search always descends into subdirectories of the starting path; there is no
          non-recursive mode.

        Output format:
        One line per match, formatted as the workspace-relative file path, a colon, the 1-based line
        number, a colon, and the matching line with surrounding whitespace trimmed. No surrounding
        context lines are returned, so follow up with read_file when you need to see the code around
        a hit.

        Usage notes:
        - Narrow with file_pattern when you know the file type, both to cut noise and to stay under
          the match cap.
        - Search for the most distinctive fragment you can. A query like `def ` will fill the result
          with noise; a query like `apply_volume_discount` will not.
        - Prefer this tool over `grep` through the shell for ordinary literal searches: paths come
          back workspace-relative, output is bounded, and an empty result is a plain message rather
          than a non-zero exit code.
        - This is a read-only operation; it never modifies the workspace.

        Limits and errors:
        - At most 200 matches are returned, and the search stops accumulating once it hits that. A
          capped result is indistinguishable from a complete one, so if you get 200 matches, narrow
          the query, the path, or the file pattern before drawing conclusions about how widely used
          something is.
        - No matches returns `no matches for '<query>'`, which is a definitive answer within the
          searched scope, not an error.
        - A starting path that does not exist returns `error: directory not found: <path>`.
        - Every file matching the pattern is read as text, so binary files in scope can produce
          meaningless matches.
        - Paths must be workspace-relative; absolute paths and `..` traversal are rejected.

        Long-session guidance:
        - Ownership and call-site questions ("who calls X", "every SaveChangesAsync site") require a
          fresh search in this turn even if you searched earlier; the tree or your question may have
          changed, and your memory of hit counts is not evidence.
        - When results look truncated or oddly few, narrow path/file_pattern or split the query
          rather than repeating the same broad search hoping for a different shape.
        - Follow each important hit with read_file before you cite it in an answer or a scratch note.
        """)]
    private string SearchFiles(
        [Description("""
            The literal text to find, for example `apply_volume_discount` or `TODO:`. Not a regular
            expression and not case-insensitive: it must appear in the line exactly as written,
            including case. Prefer a distinctive fragment over a common one so the result stays under
            the 200-match cap. For ownership questions, search the symbol as it appears in source
            (including Async suffixes and interface prefixes) rather than a paraphrased name.
            """)] string query,
        [Description("""
            Workspace-relative directory to search, searched recursively. Use `.` for the workspace
            root, which is the default. Never absolute, never containing `..`. Narrowing to a
            subdirectory such as `src` is the easiest way to cut noise when a term appears in
            documentation as well as code.
            """)] string path = ".",
        [Description("""
            Glob matched against file names only, not against directory paths, for example `*.py`,
            `*.md`, or `test_*.py`. Defaults to `*`, which searches every file under the starting
            path. Use it to restrict the search to one language or one kind of file; it does not
            accept multiple patterns, so run several searches if you need more than one extension.
            """)] string filePattern = "*")
    {
        return Guard(() =>
        {
            var full = workspace.ResolvePath(path);
            if (!Directory.Exists(full))
            {
                return $"error: directory not found: {path}";
            }

            var matches = new List<string>();
            foreach (var file in Directory.EnumerateFiles(full, filePattern, SearchOption.AllDirectories))
            {
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length && matches.Count < 200; i++)
                {
                    if (lines[i].Contains(query, StringComparison.Ordinal))
                    {
                        matches.Add($"{workspace.ToRelative(file)}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }

            return matches.Count == 0
                ? $"no matches for '{query}'"
                : string.Join(System.Environment.NewLine, matches);
        });
    }

    [Description("""
        Run a shell command inside the workspace and return its exit code together with everything it
        printed.

        The command is executed by `/bin/sh -c` on Unix-like systems, or `cmd.exe /c` on Windows,
        with the workspace root as the working directory. Shell syntax is available: pipes,
        redirection, `&&`, quoting, and environment variable expansion all work as they normally
        would.

        When to use it:
        - Running the project's build, type checker, linter, or test suite.
        - Executing a script you wrote, and reporting what it actually printed.
        - Inspecting state that no other tool exposes, such as which interpreter is on the path.

        When not to use it:
        - Do not use it for file operations that another tool already covers. Reading with `cat`,
          searching with `grep`, listing with `ls` or `find`, editing with `sed`, and writing with
          `echo` redirection or a heredoc are all worse than the dedicated tools: the output is
          unbounded, the errors are exit codes rather than messages, and heredoc writes are easy to
          get subtly wrong.
        - Do not use it to communicate with the user. Never `echo` an explanation; put your words in
          your response.

        Safety:
        - Treat this as a real machine belonging to someone else. Recursive deletion, force
          overwriting, rewriting version-control history, force pushing, changing global
          configuration, and installing software all require the user to have asked for them
          explicitly. If you think one is warranted, propose it and wait.
        - Nothing here is an OS-level sandbox. The working directory is scoped to the workspace, but
          a command is perfectly capable of reaching outside it. Stay inside deliberately.
        - Assume no network access and no package installation. A command that needs either will
          fail, and working around the failure is not the answer; say what is missing.

        Usage notes:
        - Quote every path that might contain a space.
        - Prefer non-interactive invocations. A command that waits for input will sit there until the
          timeout kills it, consuming the whole budget and returning nothing useful. Pass the flags
          that suppress prompts and pagers.
        - Chain with `&&` when the second command depends on the first, so that a failure stops the
          sequence instead of running the next step against a broken state. Run genuinely independent
          commands as separate calls so one failure does not hide another's output.
        - Prefer commands that terminate on their own. Long-lived servers and watch processes will
          simply run until the timeout.
        - Run the narrow check first and the broad one second: the single test file you touched
          before the whole suite.
        - Read the exit code, not just the text. A command that printed something reassuring and
          exited non-zero has failed. Report real output rather than paraphrasing an error.

        Output format:
        The first line is `exit=<code>`, followed by standard output and standard error concatenated
        in that order. The two streams are captured separately and joined, so interleaving does not
        reflect the true chronological order.

        Limits and errors:
        - Combined output is truncated at 20,000 characters, with a trailing line saying so. Prefer
          commands whose output is already small over piping a huge one and hoping the interesting
          part survives; note that truncation keeps the beginning, so a summary printed at the end of
          a long run can be lost.
        - The command is killed, along with any child processes, if it exceeds the harness shell
          timeout, and the call returns an error saying so instead of partial output. A timeout tells
          you nothing about whether the work succeeded.
        - A command that cannot be started at all returns an error rather than an exit code.

        Long-session guidance:
        - Use the shell for git inspection, line counts, and project-specific commands that have no
          dedicated tool. Do not use it to cat or grep files the file tools already cover.
        - In this harness a full build or test suite will usually hit the shell timeout; prefer
          reasoning from source, and say when you could not execute a check.
        - Capture command output you will need later into a scratch file with a dedicated write, not
          by relying on the truncated shell result staying in context forever.
        """)]
    private string RunShellCommand(
        [Description("""
            The command line to execute, interpreted by `/bin/sh -c` on Unix-like systems or
            `cmd.exe /c` on Windows, with the workspace root as the working directory. Shell
            features such as pipes, redirection, and `&&` are available. Use workspace-relative
            paths, quote anything containing spaces, and prefer non-interactive flags so the command
            cannot block waiting for input. Keep it to work that genuinely needs a shell: use the
            file tools for reading, writing, editing, listing, and searching.
            """)] string command)
    {
        return Guard(() =>
        {
            var isWindows = OperatingSystem.IsWindows();
            var startInfo = new ProcessStartInfo(isWindows ? "cmd.exe" : "/bin/sh")
            {
                WorkingDirectory = workspace.Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(isWindows ? "/c" : "-c");
            startInfo.ArgumentList.Add(command);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return "error: could not start a shell process.";
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit((int)shellTimeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Already exited between the wait and the kill.
                }

                return $"error: command exceeded the {shellTimeout.TotalSeconds:0}s bench shell cap and was killed.";
            }

            var output = string.Concat(stdout.Result, stderr.Result);
            if (output.Length > MaxShellOutputCharacters)
            {
                output = output[..MaxShellOutputCharacters] + $"{System.Environment.NewLine}… output truncated at {MaxShellOutputCharacters} characters.";
            }

            return $"exit={process.ExitCode}{System.Environment.NewLine}{output}";
        });
    }

    private static string Guard(Func<string> action)
    {
        try
        {
            return action();
        }
        catch (SandboxViolationException ex)
        {
            return $"error: {ex.Message}";
        }
        catch (IOException ex)
        {
            return $"error: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            return $"error: {ex.Message}";
        }
    }
}
