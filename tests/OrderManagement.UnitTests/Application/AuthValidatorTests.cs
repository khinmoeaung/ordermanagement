using FluentValidation.TestHelper;
using OrderManagement.Application.Auth;

namespace OrderManagement.UnitTests.Application;

public class AuthValidatorTests
{
    private readonly RegisterRequestValidator _register = new();

    private static RegisterRequest Valid() => new("bob@example.com", "Passw0rd1", "Bob", "555", "1 Main St");

    [Fact]
    public void Valid_request_passes() => _register.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();

    [Theory, InlineData("short1"), InlineData("lettersonly"), InlineData("12345678")]
    public void Weak_password_rejected(string p) =>
        _register.TestValidate(Valid() with { Password = p }).ShouldHaveValidationErrorFor(x => x.Password);

    [Fact]
    public void Email_must_be_valid()
    {
        _register.TestValidate(Valid() with { Email = "" }).ShouldHaveValidationErrorFor(x => x.Email);
        _register.TestValidate(Valid() with { Email = "not-an-email" }).ShouldHaveValidationErrorFor(x => x.Email);
        _register.TestValidate(Valid() with { Email = new string('a', 251) + "@x.com" }).ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void FullName_Phone_Address_required_with_max_lengths()
    {
        _register.TestValidate(Valid() with { FullName = "" }).ShouldHaveValidationErrorFor(x => x.FullName);
        _register.TestValidate(Valid() with { FullName = new string('n', 201) }).ShouldHaveValidationErrorFor(x => x.FullName);
        _register.TestValidate(Valid() with { FullName = new string('n', 200) }).ShouldNotHaveValidationErrorFor(x => x.FullName);
        _register.TestValidate(Valid() with { Phone = "" }).ShouldHaveValidationErrorFor(x => x.Phone);
        _register.TestValidate(Valid() with { Phone = new string('1', 31) }).ShouldHaveValidationErrorFor(x => x.Phone);
        _register.TestValidate(Valid() with { Phone = new string('1', 30) }).ShouldNotHaveValidationErrorFor(x => x.Phone);
        _register.TestValidate(Valid() with { ShippingAddress = "" }).ShouldHaveValidationErrorFor(x => x.ShippingAddress);
        _register.TestValidate(Valid() with { ShippingAddress = new string('a', 501) }).ShouldHaveValidationErrorFor(x => x.ShippingAddress);
        _register.TestValidate(Valid() with { ShippingAddress = new string('a', 500) }).ShouldNotHaveValidationErrorFor(x => x.ShippingAddress);
    }

    [Fact]
    public void Login_refresh_logout_require_fields()
    {
        var login = new LoginRequestValidator();
        login.TestValidate(new LoginRequest("", "x")).ShouldHaveValidationErrorFor(x => x.Email);
        login.TestValidate(new LoginRequest("a@b.com", "")).ShouldHaveValidationErrorFor(x => x.Password);
        new RefreshRequestValidator().TestValidate(new RefreshRequest("")).ShouldHaveValidationErrorFor(x => x.RefreshToken);
        new LogoutRequestValidator().TestValidate(new LogoutRequest("")).ShouldHaveValidationErrorFor(x => x.RefreshToken);
    }
}
