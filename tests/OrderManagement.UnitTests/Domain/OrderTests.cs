using System.Text.RegularExpressions;
using OrderManagement.Domain.Common;
using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;
using Shouldly;

namespace OrderManagement.UnitTests.Domain;

public class OrderTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Now.AddHours(1);

    private static Product MakeProduct(string sku, string name, decimal price) =>
        Product.Create(sku, name, null, price, 100, Now);

    private static Order MakeOrder()
    {
        var lines = new List<OrderLine>
        {
            new(MakeProduct("A-1", "Widget", 10.50m), 2),
            new(MakeProduct("B-1", "Gadget", 3.00m), 1)
        };
        return Order.Create(Guid.CreateVersion7(), lines, Now);
    }

    private static Order InStatus(OrderStatus status)
    {
        var order = MakeOrder();
        var path = new[] { OrderStatus.Confirmed, OrderStatus.Shipped, OrderStatus.Delivered, OrderStatus.Completed };
        if (status == OrderStatus.Cancelled) { order.Cancel(Now); return order; }
        Apply(order, OrderStatus.Confirmed, status, path);
        return order;
    }

    private static void Apply(Order order, OrderStatus _, OrderStatus target, OrderStatus[] path)
    {
        foreach (var step in path)
        {
            if (order.Status == target) return;
            Do(order, step switch
            {
                OrderStatus.Confirmed => "confirm",
                OrderStatus.Shipped => "ship",
                OrderStatus.Delivered => "deliver",
                _ => "complete"
            }, Now);
        }
    }

    private static void Do(Order order, string action, DateTime at)
    {
        switch (action)
        {
            case "confirm": order.Confirm(at); break;
            case "ship": order.Ship(at); break;
            case "deliver": order.Deliver(at); break;
            case "complete": order.Complete(at); break;
            case "cancel": order.Cancel(at); break;
            default: throw new ArgumentException(action);
        }
    }

    private static readonly (OrderStatus From, string Action, OrderStatus To)[] Allowed =
    [
        (OrderStatus.Pending, "confirm", OrderStatus.Confirmed),
        (OrderStatus.Confirmed, "ship", OrderStatus.Shipped),
        (OrderStatus.Shipped, "deliver", OrderStatus.Delivered),
        (OrderStatus.Delivered, "complete", OrderStatus.Completed),
        (OrderStatus.Pending, "cancel", OrderStatus.Cancelled),
        (OrderStatus.Confirmed, "cancel", OrderStatus.Cancelled)
    ];

    private static readonly string[] Actions = ["confirm", "ship", "deliver", "complete", "cancel"];

    public static IEnumerable<object[]> AllowedTransitions() =>
        Allowed.Select(t => new object[] { t.From, t.Action, t.To });

    public static IEnumerable<object[]> RejectedTransitions() =>
        from status in Enum.GetValues<OrderStatus>()
        from action in Actions
        where !Allowed.Any(t => t.From == status && t.Action == action)
        select new object[] { status, action };

    [Fact]
    public void Create_copies_name_and_price_and_computes_total()
    {
        var order = MakeOrder();
        order.TotalAmount.ShouldBe(24.00m);
        order.OrderItems.Count.ShouldBe(2);
        var first = order.OrderItems.First();
        first.ProductName.ShouldBe("Widget");
        first.UnitPrice.ShouldBe(10.50m);
        first.Quantity.ShouldBe(2);
        first.LineTotal.ShouldBe(21.00m);
        first.OrderId.ShouldBe(order.Id);
        first.ProductId.ShouldNotBe(Guid.Empty);
        first.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void Create_with_no_lines_throws() =>
        Should.Throw<DomainException>(() => Order.Create(Guid.CreateVersion7(), [], Now));

    [Fact]
    public void Create_with_non_positive_quantity_throws() =>
        Should.Throw<DomainException>(() =>
            Order.Create(Guid.CreateVersion7(), [new OrderLine(MakeProduct("A-1", "W", 1m), 0)], Now));

    [Fact]
    public void Create_starts_Pending_with_valid_number()
    {
        var order = MakeOrder();
        order.Status.ShouldBe(OrderStatus.Pending);
        Regex.IsMatch(order.OrderNumber, @"^ORD-20260101-[A-Z0-9]{6}$").ShouldBeTrue();
        order.CreatedAt.ShouldBe(Now);
        order.UpdatedAt.ShouldBe(Now);
        order.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void Copied_price_unaffected_by_later_product_update()
    {
        var product = MakeProduct("A-1", "Widget", 10m);
        var order = Order.Create(Guid.CreateVersion7(), [new OrderLine(product, 1)], Now);
        product.Update("Renamed", null, 99m, 100, Later);
        var item = order.OrderItems.Single();
        item.ProductName.ShouldBe("Widget");
        item.UnitPrice.ShouldBe(10m);
    }

    [Theory, MemberData(nameof(AllowedTransitions))]
    public void Allowed_transition_sets_status_and_UpdatedAt(OrderStatus from, string action, OrderStatus to)
    {
        var order = InStatus(from);
        Do(order, action, Later);
        order.Status.ShouldBe(to);
        order.UpdatedAt.ShouldBe(Later);
    }

    [Theory, MemberData(nameof(RejectedTransitions))]
    public void Rejected_transition_throws_conflict(OrderStatus from, string action)
    {
        var order = InStatus(from);
        var before = order.UpdatedAt;
        var ex = Should.Throw<ConflictException>(() => Do(order, action, Later));
        ex.Message.ShouldBe($"Cannot {action} an order in status {from}.");
        order.Status.ShouldBe(from);
        order.UpdatedAt.ShouldBe(before);
    }
}
