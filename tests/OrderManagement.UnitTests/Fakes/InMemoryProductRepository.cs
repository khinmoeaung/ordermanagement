using OrderManagement.Application.Common;
using OrderManagement.Application.Products;
using OrderManagement.Domain.Entities;

namespace OrderManagement.UnitTests.Fakes;

public sealed class InMemoryProductRepository : IProductRepository
{
    public List<Product> Items { get; } = [];

    public Task<Product?> GetByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Items.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<Product>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Product>>(Items.Where(p => ids.Contains(p.Id)).ToList());

    public Task<bool> SkuExistsAsync(string sku, Guid? excludeId, CancellationToken ct) =>
        Task.FromResult(Items.Any(p => p.Id != excludeId && string.Equals(p.Sku, sku, StringComparison.OrdinalIgnoreCase)));

    public void Add(Product product) => Items.Add(product);

    public Task<PagedResult<Product>> ListAsync(ProductListQuery q, bool includeInactive, CancellationToken ct)
    {
        var query = Items.Where(p => includeInactive || p.IsActive);
        if (!string.IsNullOrWhiteSpace(q.Search))
            query = query.Where(p =>
                p.Name.Contains(q.Search, StringComparison.OrdinalIgnoreCase) ||
                p.Sku.Contains(q.Search, StringComparison.OrdinalIgnoreCase));
        var all = query.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var items = all.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToList();
        return Task.FromResult(new PagedResult<Product>(items, q.Page, q.PageSize, all.Count));
    }
}
