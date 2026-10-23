using FluentValidation;
using OrderManagement.Application.Common;

namespace OrderManagement.Application.Orders;

public class PlaceOrderRequestValidator : AbstractValidator<PlaceOrderRequest>
{
    public PlaceOrderRequestValidator()
    {
        RuleFor(x => x.Items).NotEmpty();
        RuleFor(x => x.Items)
            .Must(items => items.Select(i => i.ProductId).Distinct().Count() == items.Count)
            .When(x => x.Items is not null)
            .WithMessage("Each product may appear only once.");
        RuleForEach(x => x.Items).ChildRules(item =>
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
