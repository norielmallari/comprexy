using Comprexy.Application.Models.Cost;

namespace Comprexy.Application.Abstractions;

/// <summary>
/// Read-only access to the seeded model pricing catalog (presentation cost only).
/// </summary>
public interface IModelPricingCatalogQuery
{
    Task<IReadOnlyList<ModelPricingCatalogItem>> ListActiveAsync(CancellationToken cancellationToken);
}
