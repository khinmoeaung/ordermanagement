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
}
