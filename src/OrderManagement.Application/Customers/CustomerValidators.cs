using FluentValidation;
using OrderManagement.Application.Common;

namespace OrderManagement.Application.Customers;

public class UpdateCustomerRequestValidator : AbstractValidator<UpdateCustomerRequest>
{
    public UpdateCustomerRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30);
        RuleFor(x => x.ShippingAddress).NotEmpty().MaximumLength(500);
    }
}

public class CustomerListQueryValidator : PageQueryValidator<CustomerListQuery>
{
    public CustomerListQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(200);
    }
}
