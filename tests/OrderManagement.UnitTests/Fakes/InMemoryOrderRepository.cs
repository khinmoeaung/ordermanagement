using OrderManagement.Application.Common;
using OrderManagement.Application.Orders;
using OrderManagement.Domain.Entities;

namespace OrderManagement.UnitTests.Fakes;

public sealed class InMemoryOrderRepository : IOrderRepository
{
    public List<Order> Items { get; } = [];

    public Task<Order?> GetByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Items.FirstOrDefault(o => o.Id == id));

    public Task<Order?> GetByOrderNumberAsync(string orderNumber, CancellationToken ct) =>
        Task.FromResult(Items.FirstOrDefault(o => o.OrderNumber == orderNumber));

    public void Add(Order o) => Items.Add(o);

    public Task<PagedResult<Order>> ListAsync(OrderListQuery q, Guid? customerId, CancellationToken ct)
    {
        var query = Items.AsEnumerable();
        if (customerId is not null) query = query.Where(o => o.CustomerId == customerId);
        if (q.Status is not null) query = query.Where(o => o.Status == q.Status);
        if (q.From is not null) query = query.Where(o => o.CreatedAt >= q.From);
        if (q.To is not null) query = query.Where(o => o.CreatedAt <= q.To);
        var all = query.OrderByDescending(o => o.CreatedAt).ToList();
        var items = all.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToList();
        return Task.FromResult(new PagedResult<Order>(items, q.Page, q.PageSize, all.Count));
    }
}
