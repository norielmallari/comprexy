using System.Diagnostics;
using System.Text;

namespace Comprexy.Bench.Hosting;

/// <summary>
/// A spawned Comprexy host (proxy arm or control-api) owned by the harness for one run.
/// </summary>
internal sealed class BenchHostProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly StreamWriter _log;
    private readonly object _logLock = new();

    private BenchHostProcess(string name, string baseUrl, string logPath, Process process, StreamWriter log)
    {
        Name = name;
        BaseUrl = baseUrl;
        LogPath = logPath;
        _process = process;
        _log = log;
    }

    public string Name { get; }

    public string BaseUrl { get; }

    public string LogPath { get; }

    public static async Task<BenchHostProcess> StartAsync(
        string name,
        string assemblyPath,
        string workingDirectory,
        string baseUrl,
        IReadOnlyDictionary<string, string> environment,
        string logPath,
        TimeSpan startupTimeout,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(assemblyPath);
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(baseUrl);

        foreach (var (key, value) in environment)
        {
            startInfo.Environment[key] = value;
        }

        var log = new StreamWriter(
            new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read),
            Encoding.UTF8)
        {
            AutoFlush = true
        };

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start bench host '{name}'.");

        var host = new BenchHostProcess(name, baseUrl, logPath, process, log);
        process.OutputDataReceived += (_, e) => host.AppendLog(e.Data);
        process.ErrorDataReceived += (_, e) => host.AppendLog(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await host.WaitUntilHealthyAsync(startupTimeout, cancellationToken);
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }

        return host;
    }

    private void AppendLog(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_logLock)
        {
            _log.WriteLine(line);
        }
    }

    private async Task WaitUntilHealthyAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_process.HasExited)
            {
                throw new InvalidOperationException(
                    $"Bench host '{Name}' exited with code {_process.ExitCode} before becoming healthy.{System.Environment.NewLine}{ReadLogTail()}");
            }

            try
            {
                var response = await client.GetAsync($"{BaseUrl}/health", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Kestrel is not listening yet.
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Health probe timed out; retry until the startup deadline.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        throw new TimeoutException(
            $"Bench host '{Name}' did not report healthy at {BaseUrl}/health within {timeout.TotalSeconds:0}s.{System.Environment.NewLine}{ReadLogTail()}");
    }

    public string ReadLogTail(int lines = 40)
    {
        try
        {
            lock (_logLock)
            {
                _log.Flush();
            }

            var all = File.ReadLines(LogPath).TakeLast(lines);
            return string.Join(System.Environment.NewLine, all);
        }
        catch (IOException)
        {
            return "(host log unavailable)";
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync(CancellationToken.None);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already reaped.
        }
        finally
        {
            _process.Dispose();
            await _log.DisposeAsync();
        }
    }
}
