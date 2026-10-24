using Microsoft.EntityFrameworkCore;

using OrderManagement.Application.Common;
using OrderManagement.Application.Customers;
using OrderManagement.Domain.Entities;

namespace OrderManagement.Infrastructure.Persistence.Repositories;

public sealed class CustomerRepository(AppDbContext context) : ICustomerRepository
{
    public Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct) =>
        context.Customers.Include(c => c.User).FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<Customer?> GetByUserIdAsync(Guid userId, CancellationToken ct) =>
        context.Customers.Include(c => c.User).FirstOrDefaultAsync(c => c.UserId == userId, ct);

    public void Add(Customer customer) => context.Customers.Add(customer);

    public async Task<PagedResult<Customer>> ListAsync(CustomerListQuery q, CancellationToken ct)
    {
        IQueryable<Customer> query = context.Customers.Include(c => c.User);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            string search = q.Search;
            query = query.Where(c => c.FullName.Contains(search) || c.User.Email.Contains(search));
        }

        int total = await query.CountAsync(ct);
        List<Customer> items = await query
            .OrderByDescending(c => c.User.CreatedAt)
            .ThenBy(c => c.Id)
            .Skip((q.Page - 1) * q.PageSize)
            .Take(q.PageSize)
            .ToListAsync(ct);
        return new PagedResult<Customer>(items, q.Page, q.PageSize, total);
    }
}
