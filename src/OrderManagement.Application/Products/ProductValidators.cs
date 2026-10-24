using FluentValidation;
using OrderManagement.Application.Common;

namespace OrderManagement.Application.Products;

public class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(32).Matches("^[A-Za-z0-9-]+$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Price).GreaterThan(0).PrecisionScale(18, 2, true);
        RuleFor(x => x.StockQuantity).GreaterThanOrEqualTo(0);
    }
}

public class UpdateProductRequestValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Price).GreaterThan(0).PrecisionScale(18, 2, true);
        RuleFor(x => x.StockQuantity).GreaterThanOrEqualTo(0);
    }
}

public class ProductListQueryValidator : PageQueryValidator<ProductListQuery>
{
    public ProductListQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(200);
    }
}
