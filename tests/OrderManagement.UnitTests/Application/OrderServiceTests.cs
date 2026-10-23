using Microsoft.Extensions.Time.Testing;
using OrderManagement.Application.Orders;
using OrderManagement.Domain.Common;
using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;
using OrderManagement.UnitTests.Fakes;
using Shouldly;

namespace OrderManagement.UnitTests.Application;

public class OrderServiceTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private readonly InMemoryOrderRepository _orders = new();
    private readonly InMemoryProductRepository _products = new();
    private readonly InMemoryCustomerRepository _customers = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(Now));

    private OrderService Sut(FakeCurrentUser user) => new(_orders, _products, _customers, user, _uow, _time);

    private Customer SeedCustomer(string email)
    {
        var user = User.Create(email, UserRole.Customer, Now);
        var c = Customer.Create(user, "Name " + email, "555-0100", "1 Main St");
        _customers.Add(c);
        return c;
    }

    private Product SeedProduct(string sku, decimal price, int stock)
    {
        var p = Product.Create(sku, "Product " + sku, null, price, stock, Now);
        _products.Add(p);
        return p;
    }

    private static PlaceOrderRequest Req(params (Product p, int q)[] lines) =>
        new(lines.Select(l => new PlaceOrderItem(l.p.Id, l.q)).ToList());

    private Order SeedOrder(Customer c, Product p, int qty, OrderStatus? advanceTo = null)
    {
        p.DeductStock(qty, Now);
        var o = Order.Create(c.Id, [new OrderLine(p, qty)], Now);
        if (advanceTo is >= OrderStatus.Confirmed) o.Confirm(Now);
        if (advanceTo is >= OrderStatus.Shipped) o.Ship(Now);
        if (advanceTo is >= OrderStatus.Delivered) o.Deliver(Now);
        _orders.Add(o);
        return o;
    }

    [Fact]
    public async Task Place_deducts_stock_copies_prices_and_saves_once()
    {
        var c = SeedCustomer("a@x.com");
        var p1 = SeedProduct("P1", 10.50m, 5);
        var p2 = SeedProduct("P2", 2m, 3);
        var r = await Sut(FakeCurrentUser.Customer(c.UserId)).PlaceAsync(Req((p1, 2), (p2, 3)), CancellationToken.None);

        p1.StockQuantity.ShouldBe(3);
        p2.StockQuantity.ShouldBe(0);
        r.CustomerId.ShouldBe(c.Id);
        r.Status.ShouldBe(OrderStatus.Pending);
        r.TotalAmount.ShouldBe(27m);
        r.Items.Count.ShouldBe(2);
        r.Items.Single(i => i.ProductId == p1.Id).UnitPrice.ShouldBe(10.50m);
        r.Items.Single(i => i.ProductId == p1.Id).LineTotal.ShouldBe(21m);
        _orders.Items.Count.ShouldBe(1);
        _uow.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task Place_missing_or_inactive_product_throws_not_found()
    {
        var c = SeedCustomer("a@x.com");
        var ok = SeedProduct("P1", 1m, 5);
        var inactive = SeedProduct("P2", 1m, 5);
        inactive.Deactivate(Now);
        var sut = Sut(FakeCurrentUser.Customer(c.UserId));

        await Should.ThrowAsync<NotFoundException>(() => sut.PlaceAsync(Req((ok, 1), (inactive, 1)), CancellationToken.None));
        await Should.ThrowAsync<NotFoundException>(() => sut.PlaceAsync(
            new PlaceOrderRequest([new(ok.Id, 1), new(Guid.NewGuid(), 1)]), CancellationToken.None));

        ok.StockQuantity.ShouldBe(5);
        inactive.StockQuantity.ShouldBe(5);
        _uow.SaveCount.ShouldBe(0);
        _orders.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Place_missing_product_listed_last_leaves_other_stock_unchanged()
    {
        var c = SeedCustomer("a@x.com");
        var a = SeedProduct("P1", 1m, 5);
        var b = SeedProduct("P2", 1m, 5);
        var sut = Sut(FakeCurrentUser.Customer(c.UserId));
        var req = new PlaceOrderRequest([new(a.Id, 2), new(b.Id, 2), new(Guid.NewGuid(), 1)]);
        await Should.ThrowAsync<NotFoundException>(() => sut.PlaceAsync(req, CancellationToken.None));
        a.StockQuantity.ShouldBe(5);
        b.StockQuantity.ShouldBe(5);
    }

    [Fact]
    public async Task Place_insufficient_stock_throws_conflict()
    {
        var c = SeedCustomer("a@x.com");
        var p = SeedProduct("P1", 1m, 1);
        await Should.ThrowAsync<ConflictException>(() =>
            Sut(FakeCurrentUser.Customer(c.UserId)).PlaceAsync(Req((p, 2)), CancellationToken.None));
        _uow.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task Get_other_customers_order_throws_not_found()
    {
        var owner = SeedCustomer("a@x.com");
        var other = SeedCustomer("b@x.com");
        var o = SeedOrder(owner, SeedProduct("P1", 1m, 5), 1);
        await Should.ThrowAsync<NotFoundException>(() =>
            Sut(FakeCurrentUser.Customer(other.UserId)).GetAsync(o.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Get_unknown_order_throws_not_found()
    {
        var c = SeedCustomer("a@x.com");
        await Should.ThrowAsync<NotFoundException>(() =>
            Sut(FakeCurrentUser.Customer(c.UserId)).GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Admin_can_get_any_order()
    {
        var owner = SeedCustomer("a@x.com");
        var o = SeedOrder(owner, SeedProduct("P1", 1m, 5), 1);
        var r = await Sut(FakeCurrentUser.Admin()).GetAsync(o.Id, CancellationToken.None);
        r.Id.ShouldBe(o.Id);
    }

    [Fact]
    public async Task Cancel_restores_stock_for_every_item()
    {
        var c = SeedCustomer("a@x.com");
        var p1 = SeedProduct("P1", 1m, 5);
        var p2 = SeedProduct("P2", 1m, 5);
        var placed = await Sut(FakeCurrentUser.Customer(c.UserId)).PlaceAsync(Req((p1, 2), (p2, 4)), CancellationToken.None);
        _uow.SaveCount.ShouldBe(1);

        var r = await Sut(FakeCurrentUser.Customer(c.UserId)).CancelAsync(placed.Id, CancellationToken.None);
        r.Status.ShouldBe(OrderStatus.Cancelled);
        p1.StockQuantity.ShouldBe(5);
        p2.StockQuantity.ShouldBe(5);
        _uow.SaveCount.ShouldBe(2);
    }

    [Fact]
    public async Task Cancel_restores_stock_even_if_product_deactivated()
    {
        var c = SeedCustomer("a@x.com");
        var p = SeedProduct("P1", 1m, 5);
        var o = SeedOrder(c, p, 3);
        p.Deactivate(Now);
        await Sut(FakeCurrentUser.Customer(c.UserId)).CancelAsync(o.Id, CancellationToken.None);
        p.StockQuantity.ShouldBe(5);
    }

    [Fact]
    public async Task Admin_can_cancel_any_order()
    {
        var c = SeedCustomer("a@x.com");
        var p = SeedProduct("P1", 1m, 5);
        var o = SeedOrder(c, p, 2);
        var r = await Sut(FakeCurrentUser.Admin()).CancelAsync(o.Id, CancellationToken.None);
        r.Status.ShouldBe(OrderStatus.Cancelled);
        p.StockQuantity.ShouldBe(5);
    }

    [Fact]
    public async Task Customer_cancelling_other_customers_order_throws_not_found()
    {
        var owner = SeedCustomer("a@x.com");
        var other = SeedCustomer("b@x.com");
        var p = SeedProduct("P1", 1m, 5);
        var o = SeedOrder(owner, p, 2);
        await Should.ThrowAsync<NotFoundException>(() =>
            Sut(FakeCurrentUser.Customer(other.UserId)).CancelAsync(o.Id, CancellationToken.None));
        o.Status.ShouldBe(OrderStatus.Pending);
        p.StockQuantity.ShouldBe(3);
    }

    [Fact]
    public async Task Customer_can_complete_own_delivered_order()
    {
        var c = SeedCustomer("a@x.com");
        var o = SeedOrder(c, SeedProduct("P1", 1m, 5), 1, OrderStatus.Delivered);
        var r = await Sut(FakeCurrentUser.Customer(c.UserId)).CompleteAsync(o.Id, CancellationToken.None);
        r.Status.ShouldBe(OrderStatus.Completed);
    }

    [Fact]
    public async Task Complete_before_delivered_throws_conflict()
    {
        var c = SeedCustomer("a@x.com");
        var o = SeedOrder(c, SeedProduct("P1", 1m, 5), 1);
        await Should.ThrowAsync<ConflictException>(() =>
            Sut(FakeCurrentUser.Customer(c.UserId)).CompleteAsync(o.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Admin_transitions_walk_the_workflow()
    {
        var c = SeedCustomer("a@x.com");
        var o = SeedOrder(c, SeedProduct("P1", 1m, 5), 1);
        var sut = Sut(FakeCurrentUser.Admin());
        (await sut.ConfirmAsync(o.Id, CancellationToken.None)).Status.ShouldBe(OrderStatus.Confirmed);
        (await sut.ShipAsync(o.Id, CancellationToken.None)).Status.ShouldBe(OrderStatus.Shipped);
        (await sut.DeliverAsync(o.Id, CancellationToken.None)).Status.ShouldBe(OrderStatus.Delivered);
        _uow.SaveCount.ShouldBe(3);
        await Should.ThrowAsync<NotFoundException>(() => sut.ConfirmAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ListMine_returns_only_callers_orders()
    {
        var a = SeedCustomer("a@x.com");
        var b = SeedCustomer("b@x.com");
        var p = SeedProduct("P1", 1m, 50);
        SeedOrder(a, p, 1);
        SeedOrder(b, p, 1);
        // CustomerId in the query must be ignored for /my.
        var r = await Sut(FakeCurrentUser.Customer(a.UserId))
            .ListMineAsync(new OrderListQuery { CustomerId = b.Id }, CancellationToken.None);
        r.TotalCount.ShouldBe(1);
        r.Items.Single().CustomerId.ShouldBe(a.Id);
    }

    [Fact]
    public async Task List_filters_by_customer_for_admin()
    {
        var a = SeedCustomer("a@x.com");
        var b = SeedCustomer("b@x.com");
        var p = SeedProduct("P1", 1m, 50);
        SeedOrder(a, p, 1);
        SeedOrder(b, p, 1);
        var sut = Sut(FakeCurrentUser.Admin());
        (await sut.ListAsync(new OrderListQuery(), CancellationToken.None)).TotalCount.ShouldBe(2);
        (await sut.ListAsync(new OrderListQuery { CustomerId = b.Id }, CancellationToken.None)).TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetByNumber_returns_owners_order()
    {
        var a = SeedCustomer("a@x.com");
        var o = SeedOrder(a, SeedProduct("P1", 1m, 5), 1);
        var r = await Sut(FakeCurrentUser.Customer(a.UserId)).GetByNumberAsync(o.OrderNumber, CancellationToken.None);
        r.Id.ShouldBe(o.Id);
    }

    [Fact]
    public async Task GetByNumber_other_customers_order_throws_not_found()
    {
        var a = SeedCustomer("a@x.com");
        var b = SeedCustomer("b@x.com");
        var o = SeedOrder(a, SeedProduct("P1", 1m, 5), 1);
        await Should.ThrowAsync<NotFoundException>(
            () => Sut(FakeCurrentUser.Customer(b.UserId)).GetByNumberAsync(o.OrderNumber, CancellationToken.None));
    }

    [Fact]
    public async Task GetByNumber_missing_number_throws_not_found()
    {
        var a = SeedCustomer("a@x.com");
        var b = SeedCustomer("b@x.com");
        var o = SeedOrder(a, SeedProduct("P1", 1m, 5), 1);
        var sut = Sut(FakeCurrentUser.Customer(b.UserId));

        var missing = await Should.ThrowAsync<NotFoundException>(() => sut.GetByNumberAsync("ORD-20260101-ZZZZZZ", CancellationToken.None));
        var foreign = await Should.ThrowAsync<NotFoundException>(() => sut.GetByNumberAsync(o.OrderNumber, CancellationToken.None));
        foreign.Message.ShouldBe(missing.Message);
    }

    [Fact]
    public async Task GetByNumber_admin_gets_any_order()
    {
        var a = SeedCustomer("a@x.com");
        var o = SeedOrder(a, SeedProduct("P1", 1m, 5), 1);
        var r = await Sut(FakeCurrentUser.Admin()).GetByNumberAsync(o.OrderNumber, CancellationToken.None);
        r.Id.ShouldBe(o.Id);
    }
}
