using OrderManagement.Application.Common;
using OrderManagement.Domain.Entities;

namespace OrderManagement.Application.Products;

public record ProductListQuery : PageQuery
{
    public string? Search { get; init; }
    public bool IncludeInactive { get; init; }
}

public record CreateProductRequest(string Sku, string Name, string? Description, decimal Price, int StockQuantity);

public record UpdateProductRequest(string Name, string? Description, decimal Price, int StockQuantity);

public record ProductResponse(
    Guid Id, string Sku, string Name, string? Description, decimal Price, int StockQuantity, bool IsActive);

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Product>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<bool> SkuExistsAsync(string sku, Guid? excludeId, CancellationToken ct);
    void Add(Product product);

    /// <summary>Sorted by name. Search matches Name or Sku (contains).</summary>
    Task<PagedResult<Product>> ListAsync(ProductListQuery q, bool includeInactive, CancellationToken ct);
}

public interface IProductService
{
    Task<ProductResponse> GetAsync(Guid id, CancellationToken ct);
    Task<PagedResult<ProductResponse>> ListAsync(ProductListQuery q, CancellationToken ct);
    Task<ProductResponse> CreateAsync(CreateProductRequest r, CancellationToken ct);
    Task<ProductResponse> UpdateAsync(Guid id, UpdateProductRequest r, CancellationToken ct);
    Task DeactivateAsync(Guid id, CancellationToken ct);
}
