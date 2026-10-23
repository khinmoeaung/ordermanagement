using Microsoft.Extensions.Time.Testing;
using OrderManagement.Application.Orders;
using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;
using OrderManagement.UnitTests.Fakes;
using CustomerEntity = OrderManagement.Domain.Entities.Customer;
using OrderEntity = OrderManagement.Domain.Entities.Order;

namespace OrderManagement.UnitTests.Application.Assistant;

/// <summary>A real OrderService over in-memory repositories, for assistant tool tests.</summary>
public sealed class OrderFixture
{
    public static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryOrderRepository _orders = new();
    private readonly InMemoryProductRepository _products = new();
    private readonly InMemoryCustomerRepository _customers = new();

    public OrderService ServiceFor(Customer customer) =>
        new(_orders, _products, _customers, FakeCurrentUser.Customer(customer.UserId), new FakeUnitOfWork(),
            new FakeTimeProvider(new DateTimeOffset(Now)));

    public Customer Customer(string email)
    {
        var c = CustomerEntity.Create(User.Create(email, UserRole.Customer, Now), "Name", "555-0100", "1 Secret Street");
        _customers.Add(c);
        return c;
    }

    public Order Order(Customer customer, OrderStatus status = OrderStatus.Pending, DateTime? at = null)
    {
        var p = Product.Create("SKU-" + Guid.NewGuid().ToString("N")[..8], "Blue Mug", null, 12.50m, 100, Now);
        _products.Add(p);
        var o = OrderEntity.Create(customer.Id, [new OrderLine(p, 2)], at ?? Now);
        if (status is OrderStatus.Confirmed) o.Confirm(Now);
        if (status is OrderStatus.Cancelled) o.Cancel(Now);
        _orders.Add(o);
        return o;
    }
}
