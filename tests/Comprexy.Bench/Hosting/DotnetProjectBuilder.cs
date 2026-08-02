using System.Diagnostics;

namespace Comprexy.Bench.Hosting;

/// <summary>
/// Resolves the built host assembly. The harness starts the DLL with <c>dotnet</c> rather than
/// <c>dotnet run</c> so the spawned <see cref="Process"/> owns Kestrel directly and can be killed
/// as a tree without leaving an orphaned server behind.
/// </summary>
internal static class DotnetProjectBuilder
{
    private const string Configuration = "Debug";

    public static async Task<string> ResolveAssemblyPathAsync(
        string projectFile,
        bool skipBuild,
        CancellationToken cancellationToken)
    {
        var arguments = skipBuild
            ?
            [
                "msbuild", projectFile, "-nologo",
                $"-p:Configuration={Configuration}", "-getProperty:TargetPath"
            ]
            : new[]
            {
                "build", projectFile, "-nologo", "-v", "q",
                "-c", Configuration, "--getProperty:TargetPath"
            };

        var (exitCode, stdout, stderr) = await RunAsync(arguments, cancellationToken);
        var path = stdout.Trim();
        if (exitCode != 0 || path.Length == 0 || !File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Could not build or locate {Path.GetFileName(projectFile)} (exit {exitCode}).{System.Environment.NewLine}{stdout}{stderr}");
        }

        return path;
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = BenchPaths.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the dotnet CLI.");

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, await stdout, await stderr);
    }
}
