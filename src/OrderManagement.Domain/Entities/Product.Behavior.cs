using OrderManagement.Domain.Common;

namespace OrderManagement.Domain.Entities;

public partial class Product
{
    public static string NormalizeSku(string sku) => sku.Trim().ToUpperInvariant();

    public static Product Create(string sku, string name, string? description, decimal price, int stock, DateTime utcNow)
    {
        Guard.NotBlank(sku, "SKU");
        var product = new Product
        {
            Id = Guid.CreateVersion7(),
            Sku = NormalizeSku(sku),
            IsActive = true,
            CreatedAt = utcNow
        };
        product.Apply(name, description, price, stock, utcNow);
        return product;
    }

    public void Update(string name, string? description, decimal price, int stock, DateTime utcNow) =>
        Apply(name, description, price, stock, utcNow);

    public void Deactivate(DateTime utcNow)
    {
        IsActive = false;
        UpdatedAt = utcNow;
    }

    public void DeductStock(int qty, DateTime utcNow)
    {
        RequirePositive(qty);
        if (qty > StockQuantity)
            throw new ConflictException($"Insufficient stock for SKU '{Sku}': requested {qty}, available {StockQuantity}.");
        StockQuantity -= qty;
        UpdatedAt = utcNow;
    }

    public void RestoreStock(int qty, DateTime utcNow)
    {
        RequirePositive(qty);
        StockQuantity += qty;
        UpdatedAt = utcNow;
    }

    private void Apply(string name, string? description, decimal price, int stock, DateTime utcNow)
    {
        var trimmedName = Guard.NotBlank(name, "Name");
        if (price <= 0) throw new DomainException("Price must be greater than zero.");
        if (stock < 0) throw new DomainException("Stock quantity cannot be negative.");
        Name = trimmedName;
        Description = description;
        Price = price;
        StockQuantity = stock;
        UpdatedAt = utcNow;
    }

    private static void RequirePositive(int qty)
    {
        if (qty <= 0) throw new DomainException("Quantity must be greater than zero.");
    }
}
