using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Comprexy.Application.Models;
using Comprexy.ControlApi.Contracts.Settings;
using Comprexy.Domain.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Comprexy.ControlApi.Tests;

[Collection(ControlApiSqliteCollection.Name)]
public sealed class SettingsEndpointTests
{
    private readonly ControlApiSqliteEnvGate _envGate;

    public SettingsEndpointTests(ControlApiSqliteEnvGate envGate)
    {
        _envGate = envGate;
    }

    [Fact]
    public async Task PutSettings_StaleRevision_Returns409_SuccessBumpsRevision()
    {
        await using var factory = new SettingsControlApiFactory(_envGate);
        using var client = factory.CreateClient();

        var get = await client.GetFromJsonAsync<OperatorSettingsResponseDto>("/v1/comprexy/settings");
        Assert.NotNull(get);
        Assert.Equal(0, get.Revision);

        var body = new OperatorSettingsPutRequestDto
        {
            Revision = 0,
            Settings = new OperatorMutableSettingsDto
            {
                Proxy = new ProxyMutableDto { OptimizationMode = OptimizationMode.MonitorOnly },
                ContextPolicy = new ContextPolicyMutableDto { SoftLimitTokens = 12345 }
            }
        };

        var putOk = await client.PutAsJsonAsync("/v1/comprexy/settings", body);
        Assert.Equal(HttpStatusCode.OK, putOk.StatusCode);
        var updated = await putOk.Content.ReadFromJsonAsync<OperatorSettingsResponseDto>();
        Assert.NotNull(updated);
        Assert.Equal(1, updated.Revision);
        Assert.Equal(OptimizationMode.MonitorOnly, updated.Settings.Proxy!.OptimizationMode);
        Assert.Equal(12345, updated.Settings.ContextPolicy!.SoftLimitTokens);

        var stale = new OperatorSettingsPutRequestDto
        {
            Revision = 0,
            Settings = new OperatorMutableSettingsDto
            {
                Proxy = new ProxyMutableDto { PassThrough = true }
            }
        };
        var putConflict = await client.PutAsJsonAsync("/v1/comprexy/settings", stale);
        Assert.Equal(HttpStatusCode.Conflict, putConflict.StatusCode);

        using var conflictDoc = JsonDocument.Parse(await putConflict.Content.ReadAsStringAsync());
        Assert.Equal("revision_conflict", conflictDoc.RootElement.GetProperty("error").GetString());
        Assert.Equal(1, conflictDoc.RootElement.GetProperty("currentRevision").GetInt64());

        var getAfter = await client.GetFromJsonAsync<OperatorSettingsResponseDto>("/v1/comprexy/settings");
        Assert.NotNull(getAfter);
        Assert.Equal(1, getAfter.Revision);
        Assert.Equal(OptimizationMode.MonitorOnly, getAfter.Settings.Proxy!.OptimizationMode);
    }

    [Fact]
    public async Task OperatorSettingsStore_TryPut_StaleReturnsNull()
    {
        await using var factory = new SettingsControlApiFactory(_envGate);
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<Application.Abstractions.IOperatorSettingsStore>();

        var (revision, _, _) = await store.GetAsync(CancellationToken.None);
        Assert.Equal(0, revision);

        var first = await store.TryPutAsync(
            0,
            """{"proxy":{"optimizationMode":"full"}}""",
            CancellationToken.None);
        Assert.NotNull(first);
        Assert.Equal(1, first.Value.Revision);

        var stale = await store.TryPutAsync(
            0,
            """{"proxy":{"passThrough":true}}""",
            CancellationToken.None);
        Assert.Null(stale);
    }

    private sealed class SettingsControlApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _databasePath = Path.Combine(
            Path.GetTempPath(),
            $"comprexy-settings-tests-{Guid.NewGuid():N}.db");
        private readonly IDisposable _envLease;

        public SettingsControlApiFactory(ControlApiSqliteEnvGate envGate)
        {
            _envLease = envGate.UseDatabase(_databasePath);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:RequiredApiKey"] = string.Empty,
                    ["Auth:DashboardApiKey"] = string.Empty,
                    ["Auth:ProtectV1WithDashboardKey"] = "true",
                    ["ConnectionStrings:Comprexy"] = $"Data Source={_databasePath}"
                });
            });
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            _envLease.Dispose();
            TryDelete(_databasePath);
            TryDelete($"{_databasePath}-shm");
            TryDelete($"{_databasePath}-wal");
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup for temp sqlite files.
            }
        }
    }
}
