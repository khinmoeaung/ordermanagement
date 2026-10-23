using OrderManagement.Domain.Common;

namespace OrderManagement.Domain.Entities;

public partial class OrderItem
{
    internal static OrderItem From(OrderLine line)
    {
        if (line.Quantity < 1) throw new DomainException("Quantity must be at least 1.");
        var product = line.Product;
        return new OrderItem
        {
            Id = Guid.CreateVersion7(),
            ProductId = product.Id,
            ProductName = product.Name,
            UnitPrice = product.Price,
            Quantity = line.Quantity,
            LineTotal = product.Price * line.Quantity
        };
    }
}
