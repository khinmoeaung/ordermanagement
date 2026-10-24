using FluentValidation.TestHelper;
using OrderManagement.Application.Orders;
using OrderManagement.Domain.Enums;
using Shouldly;

namespace OrderManagement.UnitTests.Application;

public class OrderValidatorTests
{
    private readonly PlaceOrderRequestValidator _place = new();
    private readonly OrderListQueryValidator _list = new();

    private static PlaceOrderRequest Req(params PlaceOrderItem[] items) => new(items);

    [Fact]
    public void Valid_request_passes() =>
        _place.TestValidate(Req(new(Guid.NewGuid(), 1), new(Guid.NewGuid(), 1000))).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Empty_items_rejected() =>
        _place.TestValidate(Req()).ShouldHaveValidationErrorFor(x => x.Items);

    [Fact]
    public void Duplicate_product_ids_rejected()
    {
        var id = Guid.NewGuid();
        _place.TestValidate(Req(new(id, 1), new(id, 2))).ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void Null_item_rejected_without_throwing()
    {
        var result = _place.TestValidate(new PlaceOrderRequest([null!]));
        result.IsValid.ShouldBeFalse();
    }

    [Theory, InlineData(0), InlineData(1001)]
    public void Quantity_out_of_range_rejected(int q) =>
        _place.TestValidate(Req(new PlaceOrderItem(Guid.NewGuid(), q))).IsValid.ShouldBeFalse();

    [Fact]
    public void Empty_product_id_rejected() =>
        _place.TestValidate(Req(new PlaceOrderItem(Guid.Empty, 1))).IsValid.ShouldBeFalse();

    [Fact]
    public void From_after_To_rejected()
    {
        var to = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _list.TestValidate(new OrderListQuery { From = to.AddDays(1), To = to }).IsValid.ShouldBeFalse();
        _list.TestValidate(new OrderListQuery { From = to, To = to }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Undefined_status_rejected() =>
        _list.TestValidate(new OrderListQuery { Status = (OrderStatus)999 }).ShouldHaveValidationErrorFor(x => x.Status);
}
