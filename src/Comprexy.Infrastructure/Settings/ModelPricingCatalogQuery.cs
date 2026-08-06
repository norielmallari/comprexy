using Comprexy.Application.Abstractions;
using Comprexy.Application.Models.Cost;
using Comprexy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Comprexy.Infrastructure.Settings;

public sealed class ModelPricingCatalogQuery : IModelPricingCatalogQuery
{
    private readonly ComprexyDbContext _db;

    public ModelPricingCatalogQuery(ComprexyDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ModelPricingCatalogItem>> ListActiveAsync(
        CancellationToken cancellationToken)
    {
        return await _db.ModelPricingEntries
            .AsNoTracking()
            .Where(e => e.IsActive)
            .OrderBy(e => e.SortOrder)
            .ThenBy(e => e.ModelKey)
            .Select(e => new ModelPricingCatalogItem
            {
                ModelKey = e.ModelKey,
                DisplayLabel = e.DisplayLabel,
                CurrencyCode = e.CurrencyCode,
                InputUsdPer1M = e.InputUsdPer1M,
                OutputUsdPer1M = e.OutputUsdPer1M,
                CachedInputUsdPer1M = e.CachedInputUsdPer1M,
                CachedOutputUsdPer1M = e.CachedOutputUsdPer1M,
                SortOrder = e.SortOrder
            })
            .ToListAsync(cancellationToken);
    }
}
