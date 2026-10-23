using OrderManagement.Domain.Common;
using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;
using Shouldly;

namespace OrderManagement.UnitTests.Domain;

public class UserTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void User_Create_normalizes_email()
    {
        var u = User.Create(" Bob@Example.com ", default(UserRole), Now);
        u.Email.ShouldBe("bob@example.com");
        u.Id.ShouldNotBe(Guid.Empty);
        u.CreatedAt.ShouldBe(Now);
    }

    [Fact]
    public void NormalizeEmail_trims_and_lowercases() =>
        User.NormalizeEmail("  A@B.COM ").ShouldBe("a@b.com");

    [Fact]
    public void SetPasswordHash_sets_and_rejects_blank()
    {
        var u = User.Create("a@b.com", default(UserRole), Now);
        u.SetPasswordHash("h");
        u.PasswordHash.ShouldBe("h");
        Should.Throw<DomainException>(() => u.SetPasswordHash(" "));
    }

    [Fact]
    public void Customer_Create_links_user_and_UpdateProfile_changes_fields()
    {
        var u = User.Create("a@b.com", default(UserRole), Now);
        var c = Customer.Create(u, "Ann", "123", "Street 1");
        c.User.ShouldBeSameAs(u);
        c.UserId.ShouldBe(u.Id);
        c.Id.ShouldNotBe(Guid.Empty);
        c.UpdateProfile("Bea", "456", "Street 2");
        c.FullName.ShouldBe("Bea");
        c.Phone.ShouldBe("456");
        c.ShippingAddress.ShouldBe("Street 2");
        Should.Throw<DomainException>(() => c.UpdateProfile("", "456", "x"));
    }
}
