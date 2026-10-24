using OrderManagement.Application.Common;
using OrderManagement.Domain.Common;

namespace OrderManagement.Application.Customers;

public class CustomerService(
    ICustomerRepository customers,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork) : ICustomerService
{
    public async Task<CustomerResponse> GetMeAsync(CancellationToken ct) =>
        (await GetCurrentAsync(ct)).ToResponse();

    public async Task<CustomerResponse> UpdateMeAsync(UpdateCustomerRequest r, CancellationToken ct)
    {
        var customer = await GetCurrentAsync(ct);
        customer.UpdateProfile(r.FullName, r.Phone, r.ShippingAddress);
        await unitOfWork.SaveChangesAsync(ct);
        return customer.ToResponse();
    }

    public async Task<CustomerResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var customer = await customers.GetByIdAsync(id, ct) ?? throw new NotFoundException("Customer", id);
        return customer.ToResponse();
    }

    public async Task<PagedResult<CustomerResponse>> ListAsync(CustomerListQuery q, CancellationToken ct)
    {
        var page = await customers.ListAsync(q, ct);
        return page.Map(c => c.ToResponse());
    }

    private async Task<Domain.Entities.Customer> GetCurrentAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var customer = userId is null ? null : await customers.GetByUserIdAsync(userId.Value, ct);
        return customer ?? throw new NotFoundException("Customer", userId ?? Guid.Empty);
    }
}
