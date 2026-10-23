using OrderManagement.Domain.Entities;

namespace OrderManagement.Application.Orders;

public static class OrderMappings
{
    public static OrderResponse ToResponse(this Order o) =>
        new(o.Id, o.OrderNumber, o.CustomerId, o.Status, o.TotalAmount, o.CreatedAt, o.UpdatedAt,
            o.OrderItems.Select(i => new OrderItemResponse(i.ProductId, i.ProductName, i.UnitPrice, i.Quantity, i.LineTotal))
                .ToList());
}
