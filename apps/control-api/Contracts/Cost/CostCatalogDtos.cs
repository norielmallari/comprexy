namespace Comprexy.ControlApi.Contracts.Cost;

public sealed record CostModelDto
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
