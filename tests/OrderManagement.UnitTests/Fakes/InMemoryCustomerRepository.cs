using OrderManagement.Application.Common;
using OrderManagement.Application.Customers;
using OrderManagement.Domain.Entities;

namespace OrderManagement.UnitTests.Fakes;

public sealed class InMemoryCustomerRepository : ICustomerRepository
{
    public List<Customer> Items { get; } = [];

    public Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Items.FirstOrDefault(c => c.Id == id));

    public Task<Customer?> GetByUserIdAsync(Guid userId, CancellationToken ct) =>
        Task.FromResult(Items.FirstOrDefault(c => c.UserId == userId));

    public void Add(Customer customer) => Items.Add(customer);

    public Task<PagedResult<Customer>> ListAsync(CustomerListQuery q, CancellationToken ct)
    {
        var query = Items.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(q.Search))
            query = query.Where(c =>
                c.FullName.Contains(q.Search, StringComparison.OrdinalIgnoreCase) ||
                c.User.Email.Contains(q.Search, StringComparison.OrdinalIgnoreCase));
        var filtered = query.OrderByDescending(c => c.User.CreatedAt).ToList();
        var items = filtered.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToList();
        return Task.FromResult(new PagedResult<Customer>(items, q.Page, q.PageSize, filtered.Count));
    }
}
