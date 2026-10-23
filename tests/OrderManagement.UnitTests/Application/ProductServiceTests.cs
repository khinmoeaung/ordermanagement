using Microsoft.Extensions.Time.Testing;
using OrderManagement.Application.Products;
using OrderManagement.Domain.Common;
using OrderManagement.Domain.Entities;
using OrderManagement.UnitTests.Fakes;
using Shouldly;

namespace OrderManagement.UnitTests.Application;

public class ProductServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryProductRepository _repo = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeTimeProvider _time = new(Now);

    private ProductService Sut(FakeCurrentUser user) => new(_repo, user, _uow, _time);

    private Product Seed(string sku, bool active = true)
    {
        var p = Product.Create(sku, "Widget " + sku, null, 9.99m, 5, Now.UtcDateTime);
        if (!active) p.Deactivate(Now.UtcDateTime);
        _repo.Add(p);
        return p;
    }

    [Fact]
    public async Task Non_admin_cannot_see_inactive_product()
    {
        var p = Seed("A-1", active: false);
        var sut = Sut(FakeCurrentUser.Customer(Guid.NewGuid()));
        await Should.ThrowAsync<NotFoundException>(() => sut.GetAsync(p.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Admin_sees_inactive_product()
    {
        var p = Seed("A-1", active: false);
        var result = await Sut(FakeCurrentUser.Admin()).GetAsync(p.Id, CancellationToken.None);
        result.Id.ShouldBe(p.Id);
        result.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task List_ignores_IncludeInactive_for_non_admin()
    {
        Seed("A-1");
        Seed("A-2", active: false);
        var q = new ProductListQuery { IncludeInactive = true };

        var customer = await Sut(FakeCurrentUser.Customer(Guid.NewGuid())).ListAsync(q, CancellationToken.None);
        var admin = await Sut(FakeCurrentUser.Admin()).ListAsync(q, CancellationToken.None);

        customer.TotalCount.ShouldBe(1);
        admin.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task Create_duplicate_sku_throws_conflict()
    {
        Seed("abc-1");
        var sut = Sut(FakeCurrentUser.Admin());
        await Should.ThrowAsync<ConflictException>(() =>
            sut.CreateAsync(new CreateProductRequest("ABC-1", "Dup", null, 1m, 1), CancellationToken.None));
        _uow.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task Create_saves_and_returns_normalized_sku()
    {
        var result = await Sut(FakeCurrentUser.Admin())
            .CreateAsync(new CreateProductRequest(" abc-2 ", "New", "d", 2.5m, 3), CancellationToken.None);
        result.Sku.ShouldBe("ABC-2");
        result.IsActive.ShouldBeTrue();
        _repo.Items.Count.ShouldBe(1);
        _uow.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task Update_missing_product_throws_not_found()
    {
        var sut = Sut(FakeCurrentUser.Admin());
        await Should.ThrowAsync<NotFoundException>(() =>
            sut.UpdateAsync(Guid.NewGuid(), new UpdateProductRequest("n", null, 1m, 1), CancellationToken.None));
    }

    [Fact]
    public async Task Update_changes_fields_and_saves()
    {
        var p = Seed("A-1");
        var result = await Sut(FakeCurrentUser.Admin())
            .UpdateAsync(p.Id, new UpdateProductRequest("Renamed", "x", 4m, 7), CancellationToken.None);
        result.Name.ShouldBe("Renamed");
        result.StockQuantity.ShouldBe(7);
        _uow.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task Deactivate_sets_inactive_and_saves()
    {
        var p = Seed("A-1");
        await Sut(FakeCurrentUser.Admin()).DeactivateAsync(p.Id, CancellationToken.None);
        p.IsActive.ShouldBeFalse();
        _uow.SaveCount.ShouldBe(1);
    }
}
