using FluentValidation;
using OrderManagement.Application.Common;

namespace OrderManagement.Application.Orders;

public class PlaceOrderRequestValidator : AbstractValidator<PlaceOrderRequest>
{
    public PlaceOrderRequestValidator()
    {
        RuleFor(x => x.Items).NotEmpty();
        RuleForEach(x => x.Items).NotNull();
        RuleFor(x => x.Items)
            .Must(items => items.Where(i => i is not null).Select(i => i.ProductId).Distinct().Count() == items.Count(i => i is not null))
            .When(x => x.Items is not null)
            .WithMessage("Each product may appear only once.");
        RuleForEach(x => x.Items).Where(i => i is not null).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.Quantity).InclusiveBetween(1, 1000);
        });
    }
}

public class OrderListQueryValidator : PageQueryValidator<OrderListQuery>
{
    public OrderListQueryValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.From).LessThanOrEqualTo(x => x.To)
            .When(x => x.From is not null && x.To is not null);
    }
}
