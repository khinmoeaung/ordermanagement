using Microsoft.EntityFrameworkCore;

using OrderManagement.Application.Common;
using OrderManagement.Application.Orders;
using OrderManagement.Domain.Entities;

namespace OrderManagement.Infrastructure.Persistence.Repositories;

public sealed class OrderRepository(AppDbContext context) : IOrderRepository
{
    public Task<Order?> GetByIdAsync(Guid id, CancellationToken ct) =>
        context.Orders.Include(o => o.OrderItems).FirstOrDefaultAsync(o => o.Id == id, ct);

    public Task<Order?> GetByOrderNumberAsync(string orderNumber, CancellationToken ct) =>
        context.Orders.Include(o => o.OrderItems).FirstOrDefaultAsync(o => o.OrderNumber == orderNumber, ct);

    public void Add(Order o) => context.Orders.Add(o);

    public async Task<PagedResult<Order>> ListAsync(OrderListQuery q, Guid? customerId, CancellationToken ct)
    {
        IQueryable<Order> query = context.Orders;
        if (customerId is not null) query = query.Where(o => o.CustomerId == customerId);
        if (q.Status is not null) query = query.Where(o => o.Status == q.Status);
        if (q.From is not null) query = query.Where(o => o.CreatedAt >= q.From);
        if (q.To is not null) query = query.Where(o => o.CreatedAt <= q.To);

        int total = await query.CountAsync(ct);
        List<Order> items = await query
            .Include(o => o.OrderItems)
            .OrderByDescending(o => o.CreatedAt)
            .ThenBy(o => o.Id)
            .Skip((q.Page - 1) * q.PageSize)
            .Take(q.PageSize)
            .ToListAsync(ct);
        return new PagedResult<Order>(items, q.Page, q.PageSize, total);
    }
}
