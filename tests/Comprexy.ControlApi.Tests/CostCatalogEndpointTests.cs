using System.Net;
using System.Net.Http.Json;
using Comprexy.ControlApi.Contracts.Cost;
using Comprexy.Domain.Entities;
using Comprexy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Comprexy.ControlApi.Tests;

[Collection(ControlApiSqliteCollection.Name)]
public sealed class CostCatalogEndpointTests
{
    private readonly ControlApiSqliteEnvGate _envGate;

    public CostCatalogEndpointTests(ControlApiSqliteEnvGate envGate)
    {
        _envGate = envGate;
    }

    private static readonly string[] ExpectedActiveKeys =
    [
        "local",
        "claude-haiku-4-5",
        "claude-sonnet-5",
        "claude-opus-5",
        "claude-fable-5",
        "gpt-5.5",
        "gpt-5.5-pro",
        "gpt-5.6-sol",
        "gpt-5.6-terra",
        "gpt-5.6-luna"
    ];

    [Fact]
    public async Task GetCostModels_AfterMigrate_ReturnsSeededLocalAndSonnetOrderedBySortOrder()
    {
        await using var factory = new CostCatalogControlApiFactory(_envGate);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/comprexy/cost-models");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var models = await response.Content.ReadFromJsonAsync<List<CostModelDto>>();
        Assert.NotNull(models);
        Assert.Equal(10, models.Count);
        Assert.Equal(ExpectedActiveKeys, models.Select(m => m.ModelKey).ToArray());

        var local = Assert.Single(models, m => m.ModelKey == "local");
        Assert.Equal("Local", local.DisplayLabel);
        Assert.Equal("USD", local.CurrencyCode);
        Assert.Equal(0m, local.InputUsdPer1M);
        Assert.Equal(0m, local.OutputUsdPer1M);
        Assert.Null(local.CachedInputUsdPer1M);
        Assert.Null(local.CachedOutputUsdPer1M);
        Assert.Equal(0, local.SortOrder);

        var sonnet = Assert.Single(models, m => m.ModelKey == "claude-sonnet-5");
        Assert.Equal(3m, sonnet.InputUsdPer1M);
        Assert.Equal(15m, sonnet.OutputUsdPer1M);
        Assert.Equal(2, sonnet.SortOrder);

        var luna = Assert.Single(models, m => m.ModelKey == "gpt-5.6-luna");
        Assert.Equal(0.20m, luna.InputUsdPer1M);
        Assert.Equal(1.20m, luna.OutputUsdPer1M);

        Assert.Contains(models, m => m.ModelKey == "gpt-5.5-pro");
        Assert.Contains(models, m => m.ModelKey == "claude-haiku-4-5");

        Assert.True(models.Select(m => m.SortOrder).SequenceEqual(models.Select(m => m.SortOrder).OrderBy(x => x)));
    }

    [Fact]
    public async Task GetCostModels_ExcludesInactiveRows()
    {
        await using var factory = new CostCatalogControlApiFactory(_envGate);
        using var client = factory.CreateClient();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ComprexyDbContext>();
            db.ModelPricingEntries.Add(ModelPricingEntry.Create(
                modelKey: "inactive-fixture",
                displayLabel: "Inactive Fixture",
                inputUsdPer1M: 9m,
                outputUsdPer1M: 9m,
                sortOrder: 99,
                isActive: false,
                id: Guid.Parse("b2000000-0000-4000-8000-000000000099")));
            await db.SaveChangesAsync();
        }

        var models = await client.GetFromJsonAsync<List<CostModelDto>>("/v1/comprexy/cost-models");
        Assert.NotNull(models);
        Assert.Equal(10, models.Count);
        Assert.DoesNotContain(models, m => m.ModelKey == "inactive-fixture");
    }

    private sealed class CostCatalogControlApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _databasePath = Path.Combine(
            Path.GetTempPath(),
            $"comprexy-cost-catalog-tests-{Guid.NewGuid():N}.db");
        private readonly IDisposable _envLease;

        public CostCatalogControlApiFactory(ControlApiSqliteEnvGate envGate)
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
            catch (IOException)
            {
                // Best-effort cleanup for temp fixture paths.
            }
        }
    }
}
