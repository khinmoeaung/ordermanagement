using OrderManagement.Domain.Entities;

namespace OrderManagement.Application.Products;

public static class ProductMappings
{
    public static ProductResponse ToResponse(this Product p) =>
        new(p.Id, p.Sku, p.Name, p.Description, p.Price, p.StockQuantity, p.IsActive);
}
