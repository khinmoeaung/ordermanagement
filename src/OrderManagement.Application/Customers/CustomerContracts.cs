using OrderManagement.Domain.Entities;

namespace OrderManagement.Application.Customers;

public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Customer?> GetByUserIdAsync(Guid userId, CancellationToken ct);
    void Add(Customer customer);
}
