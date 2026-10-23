using FluentValidation;

namespace OrderManagement.Application.Common;

/// <summary>Abstract so assembly scanning skips it; derive per query type.</summary>
public abstract class PageQueryValidator<T> : AbstractValidator<T> where T : PageQuery
{
    protected PageQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
