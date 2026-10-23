using OrderManagement.Application.Common;
using OrderManagement.Application.Customers;
using OrderManagement.Application.Products;
using OrderManagement.Domain.Common;
using OrderManagement.Domain.Entities;

namespace OrderManagement.Application.Orders;

public class OrderService(
    IOrderRepository orders,
    IProductRepository products,
    ICustomerRepository customers,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IOrderService
{
    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<OrderResponse> PlaceAsync(PlaceOrderRequest r, CancellationToken ct)
    {
        var customer = await GetCurrentCustomerAsync(ct);
        var ids = r.Items.Select(i => i.ProductId).Distinct().ToList();
        var found = (await products.GetByIdsAsync(ids, ct)).ToDictionary(p => p.Id);

        // Validate every line before mutating anything.
        foreach (var id in ids)
            if (!found.TryGetValue(id, out var p) || !p.IsActive)
                throw new NotFoundException("Product", id);

        var now = UtcNow;
        var lines = new List<OrderLine>();
        foreach (var item in r.Items)
        {
            var product = found[item.ProductId];
            product.DeductStock(item.Quantity, now);
            lines.Add(new OrderLine(product, item.Quantity));
        }

        var order = Order.Create(customer.Id, lines, now);
        orders.Add(order);
        await unitOfWork.SaveChangesAsync(ct);
        return order.ToResponse();
    }

    public async Task<OrderResponse> GetAsync(Guid id, CancellationToken ct) =>
        (await LoadAccessibleAsync(id, ct)).ToResponse();

    public async Task<PagedResult<OrderResponse>> ListMineAsync(OrderListQuery q, CancellationToken ct)
    {
        var customer = await GetCurrentCustomerAsync(ct);
        var page = await orders.ListAsync(q, customer.Id, ct);
        return page.Map(o => o.ToResponse());
    }

    public async Task<PagedResult<OrderResponse>> ListAsync(OrderListQuery q, CancellationToken ct)
    {
        var page = await orders.ListAsync(q, q.CustomerId, ct);
        return page.Map(o => o.ToResponse());
    }

    public Task<OrderResponse> ConfirmAsync(Guid id, CancellationToken ct) =>
        AdminTransitionAsync(id, (o, now) => o.Confirm(now), ct);

    public Task<OrderResponse> ShipAsync(Guid id, CancellationToken ct) =>
        AdminTransitionAsync(id, (o, now) => o.Ship(now), ct);

    public Task<OrderResponse> DeliverAsync(Guid id, CancellationToken ct) =>
        AdminTransitionAsync(id, (o, now) => o.Deliver(now), ct);

    public async Task<OrderResponse> CompleteAsync(Guid id, CancellationToken ct)
    {
        var order = await LoadAccessibleAsync(id, ct);
        order.Complete(UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
        return order.ToResponse();
    }

    public async Task<OrderResponse> CancelAsync(Guid id, CancellationToken ct)
    {
        var order = await LoadAccessibleAsync(id, ct);
        var now = UtcNow;
        order.Cancel(now);

        var ids = order.OrderItems.Select(i => i.ProductId).Distinct().ToList();
        var found = (await products.GetByIdsAsync(ids, ct)).ToDictionary(p => p.Id);
        foreach (var item in order.OrderItems)
            if (found.TryGetValue(item.ProductId, out var product))
                product.RestoreStock(item.Quantity, now);

        await unitOfWork.SaveChangesAsync(ct);
        return order.ToResponse();
    }

    private async Task<OrderResponse> AdminTransitionAsync(Guid id, Action<Order, DateTime> transition, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(id, ct) ?? throw new NotFoundException("Order", id);
        transition(order, UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
        return order.ToResponse();
    }

    private async Task<Customer> GetCurrentCustomerAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var customer = userId is null ? null : await customers.GetByUserIdAsync(userId.Value, ct);
        return customer ?? throw new NotFoundException("Customer", userId ?? Guid.Empty);
    }

    private async Task<Order> LoadAccessibleAsync(Guid id, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(id, ct) ?? throw new NotFoundException("Order", id);
        if (currentUser.IsAdmin) return order;

        var customer = await GetCurrentCustomerAsync(ct);
        if (order.CustomerId != customer.Id) throw new NotFoundException("Order", id);
        return order;
    }
}
