using OrderManagement.Domain.Common;

namespace OrderManagement.Domain.Entities;

public partial class Customer
{
    public static Customer Create(User user, string fullName, string phone, string shippingAddress)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new Customer
        {
            Id = Guid.CreateVersion7(),
            User = user,
            UserId = user.Id,
            FullName = Guard.NotBlank(fullName, "Full name"),
            Phone = Guard.NotBlank(phone, "Phone"),
            ShippingAddress = Guard.NotBlank(shippingAddress, "Shipping address")
        };
    }

    public void UpdateProfile(string fullName, string phone, string shippingAddress)
    {
        FullName = Guard.NotBlank(fullName, "Full name");
        Phone = Guard.NotBlank(phone, "Phone");
        ShippingAddress = Guard.NotBlank(shippingAddress, "Shipping address");
    }
}
