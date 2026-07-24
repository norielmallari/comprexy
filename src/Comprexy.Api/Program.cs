using Comprexy.Api.Endpoints;
using Comprexy.Api.Middleware;
using Comprexy.Application.Configuration;
using Comprexy.Application.DependencyInjection;
using Comprexy.Infrastructure.DependencyInjection;
using Comprexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Optional machine-specific overrides (gitignored). Copy from appsettings.Local.json.example.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddComprexyApplication(builder.Configuration);
builder.Services.AddComprexyInfrastructure(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ComprexyDbContext>();

    if (HasClearDatabaseArg(args))
    {
        var clearLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Comprexy");
        await DatabaseClearer.RebuildAsync(dbContext);
        clearLogger.LogInformation("Comprexy database rebuilt from migrations. Exiting.");
        return;
    }

    dbContext.Database.Migrate();
}

app.UseMiddleware<ApiKeyAuthMiddleware>();

app.MapHealthEndpoints();
app.MapChatCompletionEndpoints();
app.MapMetricsEndpoints();
app.MapPassthroughEndpoints();

var providerOptions = builder.Configuration.GetSection(ProviderOptions.SectionName).Get<ProviderOptions>() ?? new ProviderOptions();
var proxyOptions = builder.Configuration.GetSection(ProxyOptions.SectionName).Get<ProxyOptions>() ?? new ProxyOptions();
var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Comprexy");
app.Lifetime.ApplicationStarted.Register(() =>
{
    var address = app.Urls.FirstOrDefault() ?? "http://localhost:8129";
    startupLogger.LogInformation(
        """
        Comprexy is running.

        Proxy endpoint:
          {ProxyEndpoint}

        Metrics:
          {MetricsEndpoint}

        Other /v1 routes:
          reverse-proxied to upstream (passthrough)

        Configured upstream:
          {UpstreamBaseUrl} (model: {UpstreamModel})

        Chat pass-through mode:
          {PassThrough}

        Configure your OpenAI-compatible client to use:
          Base URL: {BaseUrl}
          API Key: any value, or omit (unless Auth:RequiredApiKey is configured)
        """,
        $"{address}/v1/chat/completions",
        $"{address}/v1/comprexy/conversations",
        providerOptions.BaseUrl,
        providerOptions.Model,
        proxyOptions.PassThrough ? "enabled (raw field-preserving chat proxy)" : "disabled",
        $"{address}/v1");
});

app.Run();

static bool HasClearDatabaseArg(string[] args) =>
    args.Any(arg =>
        string.Equals(arg, "--clear-db", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arg, "--clear-database", StringComparison.OrdinalIgnoreCase));
