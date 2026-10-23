using OrderManagement.Domain.Common;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Domain.Entities;

public partial class Order
{
    public static Order Create(Guid customerId, IReadOnlyList<OrderLine> lines, DateTime utcNow)
    {
        if (lines.Count == 0) throw new DomainException("An order must contain at least one item.");
        var order = new Order
        {
            Id = Guid.CreateVersion7(),
            CustomerId = customerId,
            Status = OrderStatus.Pending,
            OrderNumber = Entities.OrderNumber.Generate(utcNow),
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
        foreach (var line in lines)
        {
            var item = OrderItem.From(line);
            item.OrderId = order.Id;
            order.OrderItems.Add(item);
        }
        order.TotalAmount = order.OrderItems.Sum(i => i.LineTotal);
        return order;
    }

    public void Confirm(DateTime utcNow) => Transition("confirm", OrderStatus.Confirmed, utcNow, OrderStatus.Pending);

    public void Ship(DateTime utcNow) => Transition("ship", OrderStatus.Shipped, utcNow, OrderStatus.Confirmed);

    public void Deliver(DateTime utcNow) => Transition("deliver", OrderStatus.Delivered, utcNow, OrderStatus.Shipped);

    public void Complete(DateTime utcNow) => Transition("complete", OrderStatus.Completed, utcNow, OrderStatus.Delivered);

    public void Cancel(DateTime utcNow) =>
        Transition("cancel", OrderStatus.Cancelled, utcNow, OrderStatus.Pending, OrderStatus.Confirmed);

    private void Transition(string action, OrderStatus to, DateTime utcNow, params OrderStatus[] allowedFrom)
    {
        if (!allowedFrom.Contains(Status))
            throw new ConflictException($"Cannot {action} an order in status {Status}.");
        Status = to;
        UpdatedAt = utcNow;
    }
}
