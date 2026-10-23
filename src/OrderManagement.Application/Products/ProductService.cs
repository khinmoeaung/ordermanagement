using OrderManagement.Application.Common;
using OrderManagement.Domain.Common;
using OrderManagement.Domain.Entities;

namespace OrderManagement.Application.Products;

public class ProductService(
    IProductRepository products,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IProductService
{
    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<ProductResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var product = await products.GetByIdAsync(id, ct);
        if (product is null || (!product.IsActive && !currentUser.IsAdmin))
            throw new NotFoundException("Product", id);
        return product.ToResponse();
    }

    public async Task<PagedResult<ProductResponse>> ListAsync(ProductListQuery q, CancellationToken ct)
    {
        var page = await products.ListAsync(q, q.IncludeInactive && currentUser.IsAdmin, ct);
        return page.Map(p => p.ToResponse());
    }

    public async Task<ProductResponse> CreateAsync(CreateProductRequest r, CancellationToken ct)
    {
        var sku = Product.NormalizeSku(r.Sku);
        if (await products.SkuExistsAsync(sku, null, ct))
            throw new ConflictException($"A product with SKU '{sku}' already exists.");

        var product = Product.Create(sku, r.Name, r.Description, r.Price, r.StockQuantity, UtcNow);
        products.Add(product);
        await unitOfWork.SaveChangesAsync(ct);
        return product.ToResponse();
    }

    public async Task<ProductResponse> UpdateAsync(Guid id, UpdateProductRequest r, CancellationToken ct)
    {
        var product = await products.GetByIdAsync(id, ct) ?? throw new NotFoundException("Product", id);
        product.Update(r.Name, r.Description, r.Price, r.StockQuantity, UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
        return product.ToResponse();
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct)
    {
        var product = await products.GetByIdAsync(id, ct) ?? throw new NotFoundException("Product", id);
        product.Deactivate(UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
