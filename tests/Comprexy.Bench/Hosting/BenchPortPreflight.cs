using System.Net.NetworkInformation;
using Comprexy.Bench.Cli;

namespace Comprexy.Bench.Hosting;

/// <summary>
/// Fail fast when the fixed bench ports are already bound, before spawning Kestrel hosts.
/// </summary>
internal static class BenchPortPreflight
{
    public static void EnsurePortsFree(BenchOptions options)
    {
        if (options.NoSpawn)
        {
            return;
        }

        var required = new (string Role, int Port)[]
        {
            ("maf-compact proxy", options.MafCompactPort),
            ("control-api", options.ControlApiPort),
            ("comprexy proxy", options.ComprexyPort)
        };

        var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
        var busy = new List<string>();

        foreach (var (role, port) in required)
        {
            if (listeners.Any(endpoint => endpoint.Port == port))
            {
                busy.Add($"{role} :{port}");
            }
        }

        if (busy.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "Bench host ports are already in use: " + string.Join(", ", busy) +
            ". Stop the other writer (CLI or dashboard) or wait for .active-run.lock to clear. " +
            "Override with --proxy-port-* / --control-api-port only for deliberate multi-host setups.");
    }
}
