using Comprexy.Application.Models.Benchmarking;
using Comprexy.Application.Models.Cost;
using Comprexy.Application.Services;
using Comprexy.Application.Services.Benchmarking;

namespace Comprexy.Application.Tests.Services.Benchmarking;

public sealed class ModelPricingCostRatesMapperTests
{
    private readonly BenchmarkCostCalculator _calculator = new();

    [Fact]
    public void ToBenchmarkCostRates_ZeroInputAndOutput_MapsToLocalWithZeroCompression()
    {
        var rates = ModelPricingCostRatesMapper.ToBenchmarkCostRates(0m, 0m);

        Assert.Equal(BenchmarkModelKind.Local, rates.ModelKind);
        Assert.Equal(0m, rates.InputUsdPer1M);
        Assert.Equal(0m, rates.OutputUsdPer1M);
        Assert.Equal(0m, rates.CompressionInputUsdPer1M);
        Assert.Equal(0m, rates.CompressionOutputUsdPer1M);
    }

    [Fact]
    public void ToBenchmarkCostRates_SonnetPostSepRates_MapsToUsdWithCompressionCopiedFromMain()
    {
        var rates = ModelPricingCostRatesMapper.ToBenchmarkCostRates(3m, 15m);

        Assert.Equal(BenchmarkModelKind.Usd, rates.ModelKind);
        Assert.Equal(3m, rates.InputUsdPer1M);
        Assert.Equal(15m, rates.OutputUsdPer1M);
        Assert.Equal(3m, rates.CompressionInputUsdPer1M);
        Assert.Equal(15m, rates.CompressionOutputUsdPer1M);
    }

    [Fact]
    public void ToBenchmarkCostRates_CatalogItem_IgnoresCachedColumns()
    {
        var item = new ModelPricingCatalogItem
        {
            ModelKey = "claude-sonnet-5",
            DisplayLabel = "Claude Sonnet 5",
            CurrencyCode = "USD",
            InputUsdPer1M = 3m,
            OutputUsdPer1M = 15m,
            CachedInputUsdPer1M = 0.30m,
            CachedOutputUsdPer1M = 1.50m,
            SortOrder = 2
        };

        var rates = ModelPricingCostRatesMapper.ToBenchmarkCostRates(item);

        Assert.Equal(BenchmarkModelKind.Usd, rates.ModelKind);
        Assert.Equal(3m, rates.InputUsdPer1M);
        Assert.Equal(15m, rates.OutputUsdPer1M);
        Assert.Equal(3m, rates.CompressionInputUsdPer1M);
        Assert.Equal(15m, rates.CompressionOutputUsdPer1M);
    }

    [Fact]
    public void MapperThenCalculator_LocalRates_ProducesNoUsdBreakdown()
    {
        var rates = ModelPricingCostRatesMapper.ToBenchmarkCostRates(0m, 0m);
        var totals = new ConversationTokenTotals
        {
            ConversationId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            TurnCount = 1,
            InputTokens = 1_000_000,
            OutputTokens = 500_000,
            OverheadTokens = 10_000
        };

        var cost = _calculator.ComputeTelemetryCost(totals, rates);

        Assert.Equal(BenchmarkModelKind.Local, cost.ModelKind);
        Assert.Null(cost.CompareTotalCostUsd);
        Assert.Null(cost.CompareInputCostUsd);
        Assert.Null(cost.CompareOutputCostUsd);
    }
}
