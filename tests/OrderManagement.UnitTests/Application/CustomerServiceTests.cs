using OrderManagement.Application.Customers;
using OrderManagement.Domain.Common;
using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;
using OrderManagement.UnitTests.Fakes;
using Shouldly;

namespace OrderManagement.UnitTests.Application;

public class CustomerServiceTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private readonly InMemoryCustomerRepository _repo = new();
    private readonly FakeUnitOfWork _uow = new();

    private CustomerService Sut(FakeCurrentUser user) => new(_repo, user, _uow);

    private Customer Seed(string email, string name, int dayOffset = 0)
    {
        var user = User.Create(email, UserRole.Customer, Now.AddDays(dayOffset));
        var c = Customer.Create(user, name, "555-0100", "1 Main St");
        _repo.Add(c);
        return c;
    }

    [Fact]
    public async Task GetMe_returns_current_users_profile_with_email()
    {
        var c = Seed("ann@example.com", "Ann Lee");
        var result = await Sut(FakeCurrentUser.Customer(c.UserId)).GetMeAsync(CancellationToken.None);
        result.Id.ShouldBe(c.Id);
        result.Email.ShouldBe("ann@example.com");
        result.FullName.ShouldBe("Ann Lee");
        result.CreatedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task GetMe_without_profile_throws_not_found()
    {
        var sut = Sut(FakeCurrentUser.Customer(Guid.NewGuid()));
        await Should.ThrowAsync<NotFoundException>(() => sut.GetMeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UpdateMe_changes_profile_and_saves()
    {
        var c = Seed("ann@example.com", "Ann Lee");
        var result = await Sut(FakeCurrentUser.Customer(c.UserId))
            .UpdateMeAsync(new UpdateCustomerRequest("Ann Smith", "555-0199", "9 Side Rd"), CancellationToken.None);
        result.FullName.ShouldBe("Ann Smith");
        c.Phone.ShouldBe("555-0199");
        c.ShippingAddress.ShouldBe("9 Side Rd");
        _uow.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task Get_unknown_id_throws_not_found()
    {
        await Should.ThrowAsync<NotFoundException>(
            () => Sut(FakeCurrentUser.Admin()).GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task List_filters_by_search_and_pages()
    {
        Seed("ann@example.com", "Ann Lee", 0);
        Seed("bob@example.com", "Joanna Bob", 1);
        Seed("zed@example.com", "Zed Zed", 2);
        var result = await Sut(FakeCurrentUser.Admin())
            .ListAsync(new CustomerListQuery { Search = "ann", PageSize = 1 }, CancellationToken.None);
        result.TotalCount.ShouldBe(2);
        result.Items.Count.ShouldBe(1);
        result.Items[0].FullName.ShouldBe("Joanna Bob");
    }
}
