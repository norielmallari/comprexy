using System.Net;
using System.Net.Http.Json;
using Comprexy.Infrastructure.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;

namespace Comprexy.ControlApi.Tests;

public sealed class ProgramPipelineTests
{
    [Fact]
    public async Task RealProgramPipeline_ProtectsMcpAndV1AcceptsBothCredentialsAndLeavesHealthOpen()
    {
        await using var factory = new AuthenticatedControlApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var unauthenticatedMcp = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new { })
        };
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.SendAsync(unauthenticatedMcp)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/v1/comprexy/conversations")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

        using var rejectedHost = new HttpRequestMessage(HttpMethod.Get, "/health");
        rejectedHost.Headers.Host = "evil.example";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(rejectedHost)).StatusCode);

        using var rejectedOrigin = new HttpRequestMessage(HttpMethod.Options, "/health");
        rejectedOrigin.Headers.Add("Origin", "https://evil.example");
        rejectedOrigin.Headers.Add("Access-Control-Request-Method", "GET");
        var rejectedOriginResponse = await client.SendAsync(rejectedOrigin);
        Assert.False(rejectedOriginResponse.Headers.Contains("Access-Control-Allow-Origin"));

        var credentials = new[]
        {
            new Dictionary<string, string> { ["Authorization"] = "Bearer pipeline-secret" },
            new Dictionary<string, string>
            {
                [ApiKeyCredential.ApiKeyHeaderName] = "pipeline-secret"
            }
        };

        foreach (var headers in credentials)
        {
            await using var transport = new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint = new Uri("http://localhost/mcp"),
                    TransportMode = HttpTransportMode.StreamableHttp,
                    EnableStandaloneGetStream = false,
                    AdditionalHeaders = headers
                },
                client,
                loggerFactory: null,
                ownsHttpClient: false);
            await using var mcpClient = await McpClient.CreateAsync(transport);

            var tools = await mcpClient.ListToolsAsync();

            Assert.Equal(McpTestData.ToolNames.Order(), tools.Select(tool => tool.Name).Order());
        }
    }

    private sealed class AuthenticatedControlApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _databasePath =
            Path.Combine(Path.GetTempPath(), $"comprexy-control-api-tests-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:RequiredApiKey"] = "pipeline-secret",
                    ["ConnectionStrings:Comprexy"] = $"Data Source={_databasePath}"
                });
            });
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            File.Delete(_databasePath);
            File.Delete($"{_databasePath}-shm");
            File.Delete($"{_databasePath}-wal");
        }
    }
}
