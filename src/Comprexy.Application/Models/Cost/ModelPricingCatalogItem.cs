namespace Comprexy.Application.Models.Cost;

/// <summary>
/// Active catalog row for dashboard cost presentation (not billing).
/// </summary>
public sealed record ModelPricingCatalogItem
{
    public required string ModelKey { get; init; }

    public required string DisplayLabel { get; init; }

    public required string CurrencyCode { get; init; }

    public decimal InputUsdPer1M { get; init; }

    public decimal OutputUsdPer1M { get; init; }

    public decimal? CachedInputUsdPer1M { get; init; }

    public decimal? CachedOutputUsdPer1M { get; init; }

    public int SortOrder { get; init; }
}
