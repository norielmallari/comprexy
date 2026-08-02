using System.Diagnostics;

namespace Comprexy.Bench;

internal sealed record GitResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;

    public string FailureMessage =>
        string.IsNullOrWhiteSpace(StandardError) ? StandardOutput.Trim() : StandardError.Trim();
}

/// <summary>Runs git for the harness itself. Nothing here is exposed to the bench agent.</summary>
internal static class GitCommand
{
    public static async Task<GitResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new GitResult(-1, string.Empty, "git could not be started.");
            }

            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return new GitResult(process.ExitCode, await stdout, await stderr);
        }
        catch (SystemException ex)
        {
            return new GitResult(-1, string.Empty, ex.Message);
        }
    }
}
