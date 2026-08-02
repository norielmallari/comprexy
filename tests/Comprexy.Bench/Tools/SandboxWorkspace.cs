namespace Comprexy.Bench.Tools;

/// <summary>
/// The directory the bench agent is allowed to read, write, and run commands in: a throwaway
/// <c>git clone</c> of this repository, checked out at a pinned commit under the gitignored run
/// directory. Real Comprexy source is the point — a toy fixture cannot produce the context volume
/// the proxy exists to manage.
///
/// The clone is a full copy: its own object store (<c>--no-hardlinks</c>), its own refs, and no
/// remote, so git writes the agent makes — commits, branches, resets, <c>gc</c> — cannot reach the
/// developer's repository and cannot outlive the run. Teardown is a directory delete, which leaves
/// nothing registered anywhere even when it fails.
///
/// Every file-tool path is resolved against <see cref="Root"/> and rejected if it escapes. The shell
/// tool only gets this directory as its working directory — it is a scoping convention for the
/// benchmark, not an OS-level sandbox.
/// </summary>
internal sealed class SandboxWorkspace
{
    private SandboxWorkspace(string root, string baseCommit)
    {
        Root = root;
        BaseCommit = baseCommit;
    }

    public string Root { get; }

    /// <summary>The pinned commit the workspace started from; the diff is taken against it.</summary>
    public string BaseCommit { get; }

    /// <summary>
    /// Clones the repository and checks out <paramref name="commit"/>. Both arms are given the same
    /// commit so neither one starts from a tree the other did not see. Uncommitted work in the
    /// developer's tree is deliberately absent: the workspace is reproducible from the manifest.
    /// </summary>
    public static async Task<SandboxWorkspace> CreateAsync(
        string runDirectory,
        string armName,
        string conversationName,
        string commit,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(Path.Combine(runDirectory, "workspace", armName, conversationName));

        DeleteDirectory(root);
        Directory.CreateDirectory(Path.GetDirectoryName(root)!);

        // --no-checkout because the default branch is not the tree we want; the pinned commit is
        // checked out below. --no-hardlinks so the clone's objects are its own copies.
        var clone = await GitCommand.RunAsync(
            BenchPaths.RepoRoot,
            ["clone", "--quiet", "--no-checkout", "--no-hardlinks", BenchPaths.RepoRoot, root],
            cancellationToken);

        if (!clone.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not clone the repository into the bench workspace at {root}: {clone.FailureMessage}");
        }

        // Drop the path back to the developer's repository. Without a remote there is nothing for a
        // stray push or fetch to reach, so the clone is isolated by configuration, not by instruction.
        await GitCommand.RunAsync(root, ["remote", "remove", "origin"], cancellationToken);

        var checkout = await GitCommand.RunAsync(
            root, ["checkout", "--quiet", "--force", "-B", "bench", commit], cancellationToken);

        if (!checkout.Succeeded)
        {
            DeleteDirectory(root);
            throw new InvalidOperationException(
                $"Could not check out {commit} in the bench workspace at {root}: {checkout.FailureMessage}. " +
                "A local clone carries branches, not loose commits, so a HEAD that no branch reaches " +
                "cannot seed a workspace.");
        }

        return new SandboxWorkspace(root, commit);
    }

    /// <summary>
    /// Returns everything the agent changed as a unified diff, including files it created.
    /// <c>--intent-to-add</c> keeps untracked files in the diff without writing blobs. The diff is
    /// taken against the pinned base commit rather than <c>HEAD</c>, so an agent that commits its
    /// work does not blank out the only surviving record of what it did.
    /// </summary>
    public async Task<string> CaptureChangesAsync(CancellationToken cancellationToken)
    {
        await GitCommand.RunAsync(Root, ["add", "--all", "--intent-to-add"], cancellationToken);
        var diff = await GitCommand.RunAsync(Root, ["diff", BaseCommit], cancellationToken);
        return diff.Succeeded ? diff.StandardOutput : string.Empty;
    }

    public Task RemoveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeleteDirectory(Root);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves a model-supplied path inside the sandbox. Tool arguments are untrusted model
    /// output, so absolute paths and <c>..</c> traversal are rejected rather than clamped.
    /// <c>.</c> names the workspace root, which is the documented default for the directory tools.
    /// </summary>
    public string ResolvePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new SandboxViolationException("A path is required.");
        }

        if (relativePath is "." or "./" or ".\\")
        {
            return Root;
        }

        if (Path.IsPathRooted(relativePath))
        {
            throw new SandboxViolationException(
                $"Absolute paths are not allowed; use a path relative to the workspace root ('{relativePath}').");
        }

        var full = Path.GetFullPath(Path.Combine(Root, relativePath));
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal) && full != Root)
        {
            throw new SandboxViolationException(
                $"Path '{relativePath}' resolves outside the workspace root.");
        }

        return full;
    }

    public string ToRelative(string fullPath) =>
        Path.GetRelativePath(Root, fullPath).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>
    /// Git writes pack and object files read-only, which blocks a plain recursive delete on Windows,
    /// so attributes are cleared first.
    /// </summary>
    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(file);
            if (attributes.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
        }

        Directory.Delete(path, recursive: true);
    }
}

internal sealed class SandboxViolationException(string message) : Exception(message);
