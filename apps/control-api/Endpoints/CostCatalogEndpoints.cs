using Comprexy.Application.Abstractions;
using Comprexy.ControlApi.Contracts.Cost;

namespace Comprexy.ControlApi.Endpoints;

public static class CostCatalogEndpoints
{
    public static IEndpointRouteBuilder MapCostCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/comprexy")
            .WithTags("ComprexyCostCatalog");

        group.MapGet("/cost-models", ListCostModelsAsync);

        return app;
    }

    private static async Task<IResult> ListCostModelsAsync(
        IModelPricingCatalogQuery catalogQuery,
        CancellationToken cancellationToken)
    {
        var items = await catalogQuery.ListActiveAsync(cancellationToken);
        var dto = items.Select(item => new CostModelDto
        {
            ModelKey = item.ModelKey,
            DisplayLabel = item.DisplayLabel,
            CurrencyCode = item.CurrencyCode,
            InputUsdPer1M = item.InputUsdPer1M,
            OutputUsdPer1M = item.OutputUsdPer1M,
            CachedInputUsdPer1M = item.CachedInputUsdPer1M,
            CachedOutputUsdPer1M = item.CachedOutputUsdPer1M,
            SortOrder = item.SortOrder
        }).ToList();

        return TypedResults.Ok(dto);
    }
}
