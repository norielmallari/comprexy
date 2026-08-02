using Microsoft.Extensions.Configuration;

namespace Comprexy.Bench.Hosting;

/// <summary>
/// Values an arm's proxy actually loads, recorded in the manifest so a report never has to guess
/// what the treatment arm ran with.
/// </summary>
internal sealed record ResolvedArmConfiguration(
    string ToolSchemaMode,
    int SoftLimitTokens,
    bool PassThrough,
    string? ProviderBaseUrl,
    string? ProviderModel,
    string? RequiredApiKey);

/// <summary>
/// Rebuilds the host configuration chain (<c>appsettings.json</c> → environment-specific →
/// <c>appsettings.Local.json</c> → process environment) so the harness reads the same values the
/// spawned proxy resolves. Mirrors the provider order in <c>apps/proxy/Program.cs</c>.
/// </summary>
internal static class HostConfigurationResolver
{
    public static ResolvedArmConfiguration Resolve(
        IReadOnlyDictionary<string, string> armEnvironment,
        string environmentName)
    {
        var configuration = Build(BenchPaths.ProxyProjectDirectory, armEnvironment, environmentName);

        return new ResolvedArmConfiguration(
            configuration["ToolSchema:Mode"] ?? "Virtual",
            configuration.GetValue("ContextPolicy:SoftLimitTokens", 0),
            configuration.GetValue("Proxy:PassThrough", false),
            configuration["Provider:BaseUrl"],
            configuration["Provider:Model"],
            configuration["Auth:RequiredApiKey"]);
    }

    /// <summary>
    /// Upstream endpoint and key for the report agent, which talks to the provider directly so a
    /// report does not add conversations to the bench database. Never written to disk.
    /// </summary>
    public static (string? BaseUrl, string? ApiKey, string? Model) ResolveProvider(string environmentName)
    {
        var configuration = Build(BenchPaths.ProxyProjectDirectory, new Dictionary<string, string>(), environmentName);
        return (
            configuration["Provider:BaseUrl"],
            configuration["Provider:ApiKey"],
            configuration["Provider:Model"]);
    }

    /// <summary>
    /// Credential the spawned control-api will demand on <c>/v1/*</c>, when the operator configured one.
    /// </summary>
    public static string? ResolveControlApiKey(string environmentName) =>
        Build(BenchPaths.ControlApiProjectDirectory, new Dictionary<string, string>(), environmentName)
            ["Auth:RequiredApiKey"];

    private static IConfigurationRoot Build(
        string basePath,
        IReadOnlyDictionary<string, string> armEnvironment,
        string environmentName)
    {
        var overrides = armEnvironment
            .ToDictionary(pair => pair.Key.Replace("__", ":"), pair => (string?)pair.Value);

        return new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddInMemoryCollection(overrides)
            .Build();
    }
}
