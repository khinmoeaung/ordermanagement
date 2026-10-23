using Microsoft.EntityFrameworkCore;

using OrderManagement.Application.Common;
using OrderManagement.Application.Products;
using OrderManagement.Domain.Entities;

namespace OrderManagement.Infrastructure.Persistence.Repositories;

public sealed class ProductRepository(AppDbContext context) : IProductRepository
{
    public Task<Product?> GetByIdAsync(Guid id, CancellationToken ct) =>
        context.Products.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Product>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        Guid[] idArray = [.. ids];
        return await context.Products.Where(p => idArray.Contains(p.Id)).ToListAsync(ct);
    }

    public Task<bool> SkuExistsAsync(string sku, Guid? excludeId, CancellationToken ct) =>
        context.Products.AnyAsync(p => p.Sku == sku && p.Id != excludeId, ct);

    public void Add(Product product) => context.Products.Add(product);

    public async Task<PagedResult<Product>> ListAsync(ProductListQuery q, bool includeInactive, CancellationToken ct)
    {
        IQueryable<Product> query = context.Products.Where(p => includeInactive || p.IsActive);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            string search = q.Search;
            query = query.Where(p => p.Name.Contains(search) || p.Sku.Contains(search));
        }

        int total = await query.CountAsync(ct);
        List<Product> items = await query
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Skip((q.Page - 1) * q.PageSize)
            .Take(q.PageSize)
            .ToListAsync(ct);
        return new PagedResult<Product>(items, q.Page, q.PageSize, total);
    }
}
