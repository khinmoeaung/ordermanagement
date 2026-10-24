using OrderManagement.Domain.Entities;

namespace OrderManagement.Application.Customers;

public static class CustomerMappings
{
    public static CustomerResponse ToResponse(this Customer c) =>
        new(c.Id, c.User.Email, c.FullName, c.Phone, c.ShippingAddress, c.User.CreatedAt);
}
