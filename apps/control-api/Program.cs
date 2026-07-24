using Comprexy.Application.DependencyInjection;
using Comprexy.ControlApi.Endpoints;
using Comprexy.Infrastructure.DependencyInjection;
using Comprexy.Infrastructure.Hosting;
using Comprexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Shared SQLite under repo data/ (both hosts). Optional Local.json may override ConnectionStrings.
SharedSqliteConfiguration.UseRepoSharedDatabase(builder);

// Optional machine-specific overrides (gitignored). Copy from appsettings.Local.json.example.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddComprexyApplication(builder.Configuration, enableProxyServices: false);
builder.Services.AddComprexyInfrastructure(builder.Configuration, enableCompressionWorker: false);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ComprexyDbContext>();
    dbContext.Database.Migrate();
}

app.UseMiddleware<ApiKeyAuthMiddleware>();

app.MapHealthEndpoints();
app.MapMetricsEndpoints();

var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Comprexy.ControlApi");
app.Lifetime.ApplicationStarted.Register(() =>
{
    var address = app.Urls.FirstOrDefault() ?? "http://localhost:8130";
    startupLogger.LogInformation(
        """
        Comprexy control-api is running.

        Metrics:
          {MetricsEndpoint}

        Health:
          {HealthEndpoint}
        """,
        $"{address}/v1/comprexy/conversations",
        $"{address}/health");
});

app.Run();
