using FluentValidation.TestHelper;
using OrderManagement.Application.Products;
using Shouldly;

namespace OrderManagement.UnitTests.Application;

public class ProductValidatorTests
{
    private readonly CreateProductRequestValidator _create = new();
    private readonly ProductListQueryValidator _list = new();

    private static CreateProductRequest Valid() => new("ABC-1", "Widget", "desc", 9.99m, 1);

    [Fact]
    public void Valid_request_passes() => _create.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();

    [Theory, InlineData(0), InlineData(-1)]
    public void Price_must_be_positive(decimal p) =>
        _create.TestValidate(Valid() with { Price = p }).ShouldHaveValidationErrorFor(x => x.Price);

    [Fact]
    public void Price_with_more_than_two_decimals_is_rejected() =>
        _create.TestValidate(Valid() with { Price = 19.999m }).ShouldHaveValidationErrorFor(x => x.Price);

    [Fact]
    public void Price_with_trailing_zeros_is_accepted() =>
        _create.TestValidate(Valid() with { Price = 19.990m }).ShouldNotHaveValidationErrorFor(x => x.Price);

    [Fact]
    public void Sku_required_max_32_letters_digits_dash()
    {
        _create.TestValidate(Valid() with { Sku = "" }).ShouldHaveValidationErrorFor(x => x.Sku);
        _create.TestValidate(Valid() with { Sku = new string('A', 33) }).ShouldHaveValidationErrorFor(x => x.Sku);
        _create.TestValidate(Valid() with { Sku = "AB C" }).ShouldHaveValidationErrorFor(x => x.Sku);
        _create.TestValidate(Valid() with { Sku = "AB_C" }).ShouldHaveValidationErrorFor(x => x.Sku);
        _create.TestValidate(Valid() with { Sku = new string('A', 32) }).ShouldNotHaveValidationErrorFor(x => x.Sku);
    }

    [Fact]
    public void Name_required_max_200()
    {
        _create.TestValidate(Valid() with { Name = "" }).ShouldHaveValidationErrorFor(x => x.Name);
        _create.TestValidate(Valid() with { Name = new string('n', 201) }).ShouldHaveValidationErrorFor(x => x.Name);
        _create.TestValidate(Valid() with { Name = new string('n', 200) }).ShouldNotHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Description_max_2000() =>
        _create.TestValidate(Valid() with { Description = new string('d', 2001) })
            .ShouldHaveValidationErrorFor(x => x.Description);

    [Fact]
    public void Stock_must_be_zero_or_more()
    {
        _create.TestValidate(Valid() with { StockQuantity = -1 }).ShouldHaveValidationErrorFor(x => x.StockQuantity);
        _create.TestValidate(Valid() with { StockQuantity = 0 }).ShouldNotHaveValidationErrorFor(x => x.StockQuantity);
    }

    [Fact]
    public void Update_validator_applies_same_rules()
    {
        var v = new UpdateProductRequestValidator();
        v.TestValidate(new UpdateProductRequest("", null, 19.999m, -1)).Errors.Count.ShouldBe(3);
    }

    [Theory, InlineData(0, 20), InlineData(1, 0), InlineData(1, 101)]
    public void Page_bounds_rejected(int page, int size) =>
        _list.TestValidate(new ProductListQuery { Page = page, PageSize = size }).IsValid.ShouldBeFalse();

    [Fact]
    public void Page_bounds_accepted() =>
        _list.TestValidate(new ProductListQuery { Page = 1, PageSize = 100 }).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Search_max_200() =>
        _list.TestValidate(new ProductListQuery { Search = new string('s', 201) })
            .ShouldHaveValidationErrorFor(x => x.Search);
}
