using Comprexy.Bench.Cli;

namespace Comprexy.Bench.Hosting;

/// <summary>
/// Owns the processes for one bench run: one control-api plus one proxy per arm, all pointed at
/// the same bench SQLite file. Hosts start one at a time so only one of them runs
/// <c>Database.Migrate()</c> against a fresh file.
/// </summary>
internal sealed class BenchHostFleet : IAsyncDisposable
{
    private readonly List<BenchHostProcess> _hosts = [];
    private readonly BenchOptions _options;
    private readonly string _logDirectory;

    private BenchHostFleet(BenchOptions options, string logDirectory)
    {
        _options = options;
        _logDirectory = logDirectory;
    }

    public string ControlApiBaseUrl => $"http://127.0.0.1:{_options.ControlApiPort}";

    public static async Task<BenchHostFleet> StartAsync(
        BenchOptions options,
        IReadOnlyList<BenchArm> arms,
        CancellationToken cancellationToken,
        string logSubdirectory = "logs")
    {
        var logDirectory = Path.Combine(options.RunDirectory, logSubdirectory);
        var fleet = new BenchHostFleet(options, logDirectory);

        if (options.NoSpawn)
        {
            Console.Error.WriteLine(
                $"--no-spawn: expecting a control-api on {fleet.ControlApiBaseUrl} and a proxy per arm already running.");
            return fleet;
        }

        try
        {
            var controlApiAssembly = await DotnetProjectBuilder.ResolveAssemblyPathAsync(
                BenchPaths.ControlApiProjectFile, options.SkipBuild, cancellationToken);
            var proxyAssembly = await DotnetProjectBuilder.ResolveAssemblyPathAsync(
                BenchPaths.ProxyProjectFile, options.SkipBuild, cancellationToken);

            fleet._hosts.Add(await BenchHostProcess.StartAsync(
                "control-api",
                controlApiAssembly,
                BenchPaths.ControlApiProjectDirectory,
                fleet.ControlApiBaseUrl,
                fleet.SharedEnvironment(),
                Path.Combine(logDirectory, "control-api.log"),
                TimeSpan.FromSeconds(options.HostStartupTimeoutSeconds),
                cancellationToken));

            foreach (var arm in arms)
            {
                fleet._hosts.Add(await BenchHostProcess.StartAsync(
                    arm.Name,
                    proxyAssembly,
                    BenchPaths.ProxyProjectDirectory,
                    arm.BaseUrl,
                    fleet.ArmEnvironment(arm),
                    Path.Combine(logDirectory, $"{arm.Name}.log"),
                    TimeSpan.FromSeconds(options.HostStartupTimeoutSeconds),
                    cancellationToken));
            }
        }
        catch
        {
            await fleet.DisposeAsync();
            throw;
        }

        return fleet;
    }

    public IReadOnlyDictionary<string, string> SharedEnvironment() => new Dictionary<string, string>
    {
        ["ASPNETCORE_ENVIRONMENT"] = "Development",
        ["ConnectionStrings__Comprexy"] = $"Data Source={_options.DatabasePath};Cache=Shared",
        // Bench spend reporting uses upstream usage.prompt_tokens when present (both arms).
        ["Metrics__PromptTokenBasis"] = "ProviderActual"
    };

    public IReadOnlyDictionary<string, string> ArmEnvironment(BenchArm arm)
    {
        var environment = new Dictionary<string, string>(SharedEnvironment());
        foreach (var (key, value) in arm.Environment)
        {
            environment[key] = value;
        }

        if (_options.Trace)
        {
            environment["Trace__RequestFiles"] = "true";
            environment["Trace__RequestLogDirectory"] =
                Path.Combine(_options.RunDirectory, "traces", arm.Name);
        }

        return environment;
    }

    public string ReadArmLogTail(string armName) =>
        _hosts.FirstOrDefault(h => h.Name == armName)?.ReadLogTail()
        ?? "(arm not spawned by this harness)";

    public async ValueTask DisposeAsync()
    {
        // Reverse order: proxies first, control-api last, so late writes still have a live DB host.
        for (var i = _hosts.Count - 1; i >= 0; i--)
        {
            await _hosts[i].DisposeAsync();
        }

        _hosts.Clear();
    }
}
