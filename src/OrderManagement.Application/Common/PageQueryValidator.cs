using FluentValidation;

namespace OrderManagement.Application.Common;

/// <summary>Abstract so assembly scanning skips it; derive per query type.</summary>
public abstract class PageQueryValidator<T> : AbstractValidator<T> where T : PageQuery
{
    private const int MaxPage = 1_000_000;

    protected PageQueryValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, MaxPage);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
