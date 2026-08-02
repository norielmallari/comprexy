namespace Comprexy.Bench.Running;

/// <summary>
/// Deterministically selects the largest repository files for bench prompts — size descending,
/// then relative path ascending. Reads from <see cref="BenchPaths.RepoRoot"/> at script load time.
/// </summary>
internal static class RepoLargestFilesSelector
{
    internal sealed record Options(
        int Count,
        IReadOnlyList<string> ExcludeDirectoryNames,
        IReadOnlyList<string> Extensions,
        IReadOnlyList<string> ExcludeFileNames);

    internal sealed record SelectedFile(string RelativePath, string Contents, long ByteLength);

    public static IReadOnlyList<SelectedFile> Select(Options options)
    {
        if (options.Count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "largestFiles.count must be positive.");
        }

        var root = BenchPaths.RepoRoot;
        var candidates = new List<(string RelativePath, long ByteLength)>();

        foreach (var absolutePath in Directory.EnumerateFiles(
                     root,
                     "*",
                     new EnumerationOptions
                     {
                         RecurseSubdirectories = true,
                         IgnoreInaccessible = true,
                         AttributesToSkip = FileAttributes.Device,
                     }))
        {
            var relativePath = Path.GetRelativePath(root, absolutePath).Replace('\\', '/');
            if (IsExcluded(relativePath, options))
            {
                continue;
            }

            if (!options.Extensions.Any(ext =>
                    relativePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var fileName = Path.GetFileName(relativePath);
            if (options.ExcludeFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var length = new FileInfo(absolutePath).Length;
            if (length == 0)
            {
                continue;
            }

            candidates.Add((relativePath, length));
        }

        var selected = candidates
            .OrderByDescending(c => c.ByteLength)
            .ThenBy(c => c.RelativePath, StringComparer.Ordinal)
            .Take(options.Count)
            .ToList();

        if (selected.Count == 0)
        {
            throw new InvalidOperationException(
                "largestFiles found no matching repository files. Relax extensions or exclude rules.");
        }

        return selected
            .Select(c => new SelectedFile(
                c.RelativePath,
                File.ReadAllText(Path.Combine(root, c.RelativePath)),
                c.ByteLength))
            .ToList();
    }

    private static bool IsExcluded(string relativePath, Options options)
    {
        foreach (var segment in relativePath.Split('/'))
        {
            if (options.ExcludeDirectoryNames.Contains(segment, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
