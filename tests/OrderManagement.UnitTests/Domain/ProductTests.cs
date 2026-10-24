using OrderManagement.Domain.Common;
using OrderManagement.Domain.Entities;
using Shouldly;

namespace OrderManagement.UnitTests.Domain;

public class ProductTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Now.AddHours(1);

    private static Product Make(int stock) => Product.Create(" ab-1 ", "Widget", null, 10m, stock, Now);

    [Fact]
    public void Create_trims_and_uppercases_sku_and_sets_defaults()
    {
        var p = Make(5);
        p.Sku.ShouldBe("AB-1");
        p.IsActive.ShouldBeTrue();
        p.Id.ShouldNotBe(Guid.Empty);
        p.CreatedAt.ShouldBe(Now);
        p.UpdatedAt.ShouldBe(Now);
    }

    [Fact]
    public void NormalizeSku_trims_and_uppercases() => Product.NormalizeSku("  ab-c ").ShouldBe("AB-C");

    [Fact]
    public void DeductStock_reduces_stock()
    {
        var p = Make(5);
        p.DeductStock(3, Later);
        p.StockQuantity.ShouldBe(2);
        p.UpdatedAt.ShouldBe(Later);
    }

    [Fact]
    public void DeductStock_below_zero_throws_conflict()
    {
        var p = Make(2);
        Should.Throw<ConflictException>(() => p.DeductStock(3, Later));
        p.StockQuantity.ShouldBe(2);
    }

    [Fact]
    public void RestoreStock_adds_back()
    {
        var p = Make(2);
        p.RestoreStock(3, Later);
        p.StockQuantity.ShouldBe(5);
    }

    [Theory, InlineData(0), InlineData(-1)]
    public void DeductStock_non_positive_qty_throws(int q) =>
        Should.Throw<DomainException>(() => Make(5).DeductStock(q, Later));

    [Theory, InlineData(0), InlineData(-1)]
    public void RestoreStock_non_positive_qty_throws(int q) =>
        Should.Throw<DomainException>(() => Make(5).RestoreStock(q, Later));

    [Fact]
    public void Create_with_non_positive_price_throws() =>
        Should.Throw<DomainException>(() => Product.Create("A", "N", null, 0m, 1, Now));

    [Fact]
    public void Create_with_negative_stock_throws() =>
        Should.Throw<DomainException>(() => Product.Create("A", "N", null, 1m, -1, Now));

    [Fact]
    public void Create_with_blank_name_throws() =>
        Should.Throw<DomainException>(() => Product.Create("A", " ", null, 1m, 1, Now));

    [Fact]
    public void Update_changes_fields_and_validates()
    {
        var p = Make(1);
        p.Update("New", "d", 3m, 7, Later);
        p.Name.ShouldBe("New");
        p.Description.ShouldBe("d");
        p.Price.ShouldBe(3m);
        p.StockQuantity.ShouldBe(7);
        p.UpdatedAt.ShouldBe(Later);
        Should.Throw<DomainException>(() => p.Update("New", null, -1m, 1, Later));
    }

    [Fact]
    public void Deactivate_sets_IsActive_false_and_UpdatedAt()
    {
        var p = Make(1);
        p.Deactivate(Later);
        p.IsActive.ShouldBeFalse();
        p.UpdatedAt.ShouldBe(Later);
    }
}
