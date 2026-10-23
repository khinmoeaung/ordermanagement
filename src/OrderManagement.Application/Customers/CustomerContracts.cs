using OrderManagement.Application.Common;
using OrderManagement.Domain.Entities;

namespace OrderManagement.Application.Customers;

public record CustomerListQuery : PageQuery
{
    public string? Search { get; init; }
}

public record UpdateCustomerRequest(string FullName, string Phone, string ShippingAddress);

public record CustomerResponse(
    Guid Id, string Email, string FullName, string Phone, string ShippingAddress, DateTime CreatedAt);

public interface ICustomerRepository
{
    /// <summary>Includes the User.</summary>
    Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Includes the User.</summary>
    Task<Customer?> GetByUserIdAsync(Guid userId, CancellationToken ct);
    void Add(Customer customer);

    /// <summary>Includes the User. Sorted newest first by User.CreatedAt. Search matches FullName or User.Email (contains).</summary>
    Task<PagedResult<Customer>> ListAsync(CustomerListQuery q, CancellationToken ct);
}

public interface ICustomerService
{
    Task<CustomerResponse> GetMeAsync(CancellationToken ct);
    Task<CustomerResponse> UpdateMeAsync(UpdateCustomerRequest r, CancellationToken ct);
    Task<CustomerResponse> GetAsync(Guid id, CancellationToken ct);
    Task<PagedResult<CustomerResponse>> ListAsync(CustomerListQuery q, CancellationToken ct);
}
