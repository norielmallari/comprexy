using Comprexy.Application.DependencyInjection;
using Comprexy.ControlApi.Benchmarking;
using Comprexy.ControlApi.Configuration;
using Comprexy.ControlApi.Endpoints;
using Comprexy.ControlApi.Mcp;
using Comprexy.ControlApi.Mcp.Resources;
using Comprexy.ControlApi.Mcp.Tools;
using Comprexy.Infrastructure.DependencyInjection;
using Comprexy.Infrastructure.Hosting;
using Comprexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Shared SQLite under repo data/ (both hosts). Optional Local.json may override ConnectionStrings.
SharedSqliteConfiguration.UseRepoSharedDatabase(builder);

// Optional machine-specific overrides (gitignored). Copy from appsettings.Local.json.example.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Re-append so env/cmdline still win over SharedSqlite and Local.json (harness and container overrides).
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(args);

builder.Services.AddComprexyApplication(builder.Configuration, enableProxyServices: false);
builder.Services.AddComprexyInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddOptions<McpTelemetryOptions>()
    .Bind(builder.Configuration.GetSection(McpTelemetryOptions.SectionName));
builder.Services.AddOptions<BenchOrchestrationOptions>()
    .Bind(builder.Configuration.GetSection(BenchOrchestrationOptions.SectionName));
builder.Services.AddSingleton<IBenchProcessRunner, DotNetBenchProcessRunner>();
builder.Services.AddSingleton<IBenchRunOrchestrator, BenchRunOrchestrator>();
builder.Services.AddScoped<BenchmarkPresentationService>();
builder.Services.AddSingleton<McpToolCallAuditLogger>();

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (corsOrigins.Length == 0)
        {
            // Secure default: deny browser cross-origin access (server-side MCP clients are unaffected).
            policy.SetIsOriginAllowed(_ => false);
        }
        else
        {
            policy.WithOrigins(corsOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<ConversationTools>()
    .WithTools<ConversationRetrievalTools>()
    .WithResources<ConversationResources>()
    .WithResources<ConversationRetrievalResources>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ComprexyDbContext>();
    dbContext.Database.Migrate();
}

app.UseCors();
app.UseMiddleware<ApiKeyAuthMiddleware>();

app.MapHealthEndpoints();
app.MapMetricsEndpoints();
app.MapBenchmarkEndpoints();
app.MapCostCatalogEndpoints();
app.MapSettingsEndpoints();
app.MapMcp("/mcp");

var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Comprexy.ControlApi");
app.Lifetime.ApplicationStarted.Register(() =>
{
    var address = app.Urls.FirstOrDefault() ?? "http://localhost:8130";
    startupLogger.LogInformation(
        """
        Comprexy control-api is running.

        Metrics:
          {MetricsEndpoint}

        MCP (Streamable HTTP):
          {McpEndpoint}

        Health:
          {HealthEndpoint}
        """,
        $"{address}/v1/comprexy/conversations",
        $"{address}/mcp",
        $"{address}/health");
});

app.Run();

/// <summary>
/// Marker for <c>WebApplicationFactory&lt;Program&gt;</c> integrated pipeline tests.
/// </summary>
public partial class Program;
