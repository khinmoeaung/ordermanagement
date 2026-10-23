using FluentValidation.TestHelper;
using OrderManagement.Application.Customers;

namespace OrderManagement.UnitTests.Application;

public class CustomerValidatorTests
{
    private readonly UpdateCustomerRequestValidator _update = new();
    private readonly CustomerListQueryValidator _list = new();

    private static UpdateCustomerRequest Valid() => new("Ann Lee", "555-0100", "1 Main St");

    [Fact]
    public void Valid_request_passes() => _update.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();

    [Theory, InlineData(""), InlineData("  ")]
    public void Blank_values_are_rejected(string blank)
    {
        _update.TestValidate(Valid() with { FullName = blank }).ShouldHaveValidationErrorFor(x => x.FullName);
        _update.TestValidate(Valid() with { Phone = blank }).ShouldHaveValidationErrorFor(x => x.Phone);
        _update.TestValidate(Valid() with { ShippingAddress = blank }).ShouldHaveValidationErrorFor(x => x.ShippingAddress);
    }

    [Fact]
    public void Over_limit_values_are_rejected()
    {
        _update.TestValidate(Valid() with { FullName = new string('a', 201) }).ShouldHaveValidationErrorFor(x => x.FullName);
        _update.TestValidate(Valid() with { Phone = new string('1', 31) }).ShouldHaveValidationErrorFor(x => x.Phone);
        _update.TestValidate(Valid() with { ShippingAddress = new string('a', 501) }).ShouldHaveValidationErrorFor(x => x.ShippingAddress);
    }

    [Fact]
    public void At_limit_values_pass() =>
        _update.TestValidate(new UpdateCustomerRequest(new string('a', 200), new string('1', 30), new string('a', 500)))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Search_over_200_is_rejected() =>
        _list.TestValidate(new CustomerListQuery { Search = new string('a', 201) }).ShouldHaveValidationErrorFor(x => x.Search);
}
