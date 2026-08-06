namespace Comprexy.Domain.Entities;

/// <summary>
/// Seeded presentation cost rates for operator-selected models (not billing).
/// Calculations use <see cref="InputUsdPer1M"/> and <see cref="OutputUsdPer1M"/> only;
/// cached columns are reserved for a future token-count path.
/// </summary>
public class ModelPricingEntry : EntityBase
{
    public string ModelKey { get; private set; } = string.Empty;

    public string DisplayLabel { get; private set; } = string.Empty;

    public string CurrencyCode { get; private set; } = "USD";

    public decimal InputUsdPer1M { get; private set; }

    public decimal OutputUsdPer1M { get; private set; }

    public decimal? CachedInputUsdPer1M { get; private set; }

    public decimal? CachedOutputUsdPer1M { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; }

    private ModelPricingEntry()
    {
    }

    public static ModelPricingEntry Create(
        string modelKey,
        string displayLabel,
        decimal inputUsdPer1M,
        decimal outputUsdPer1M,
        int sortOrder,
        string currencyCode = "USD",
        decimal? cachedInputUsdPer1M = null,
        decimal? cachedOutputUsdPer1M = null,
        bool isActive = true,
        Guid? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(currencyCode);

        return new ModelPricingEntry
        {
            Id = id ?? Guid.NewGuid(),
            ModelKey = modelKey.Trim(),
            DisplayLabel = displayLabel.Trim(),
            CurrencyCode = currencyCode.Trim(),
            InputUsdPer1M = inputUsdPer1M,
            OutputUsdPer1M = outputUsdPer1M,
            CachedInputUsdPer1M = cachedInputUsdPer1M,
            CachedOutputUsdPer1M = cachedOutputUsdPer1M,
            SortOrder = sortOrder,
            IsActive = isActive
        };
    }
}
