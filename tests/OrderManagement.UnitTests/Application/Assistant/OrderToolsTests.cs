using System.Text.Json;
using System.Text.Json.Serialization;
using OrderManagement.Application.Assistant;
using OrderManagement.Application.Assistant.Tools;
using OrderManagement.Domain.Common;
using OrderManagement.Domain.Enums;
using Shouldly;

namespace OrderManagement.UnitTests.Application.Assistant;

public class OrderToolsTests
{
    private readonly OrderFixture _f = new();
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task GetOrderStatus_normalizes_input()
    {
        var a = _f.Customer("a@x.com");
        var o = _f.Order(a);
        var tool = new GetOrderStatusTool(_f.ServiceFor(a));

        var result = (OrderStatusResult)await tool.ExecuteAsync(
            Json($$"""{"orderNumber":" {{o.OrderNumber.ToLowerInvariant()}} "}"""), Ct);

        result.OrderNumber.ShouldBe(o.OrderNumber);
        result.Status.ShouldBe(OrderStatus.Pending);
        result.TotalAmount.ShouldBe(25m);
        result.Items.ShouldHaveSingleItem().ShouldBe(new OrderStatusItem("Blue Mug", 2));
    }

    [Theory]
    [InlineData("""{"orderNumber":"123"}""")]
    [InlineData("""{"orderNumber":"ORD-2026-ABCDEF"}""")]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task GetOrderStatus_rejects_malformed_input(string json)
    {
        var a = _f.Customer("a@x.com");
        await Should.ThrowAsync<ToolInputException>(() => new GetOrderStatusTool(_f.ServiceFor(a)).ExecuteAsync(Json(json), Ct));
    }

    [Fact]
    public async Task GetOrderStatus_other_customers_order_throws_not_found()
    {
        var a = _f.Customer("a@x.com");
        var b = _f.Customer("b@x.com");
        var o = _f.Order(a);

        await Should.ThrowAsync<NotFoundException>(() =>
            new GetOrderStatusTool(_f.ServiceFor(b)).ExecuteAsync(Json($$"""{"orderNumber":"{{o.OrderNumber}}"}"""), Ct));
    }

    [Fact]
    public async Task GetOrderStatus_result_contains_only_restricted_fields()
    {
        var a = _f.Customer("a@x.com");
        var o = _f.Order(a);
        var result = await new GetOrderStatusTool(_f.ServiceFor(a)).ExecuteAsync(Json($$"""{"orderNumber":"{{o.OrderNumber}}"}"""), Ct);

        string json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        foreach (string forbidden in new[] { "\"id\"", "customerId", "productId", "unitPrice", "lineTotal", "address", "Secret" })
            json.ShouldNotContain(forbidden, Case.Insensitive);
        json.ShouldNotContain(o.Id.ToString());
        json.ShouldNotContain(a.Id.ToString());
    }

    [Fact]
    public async Task ListMyOrders_filters_by_status_and_caps_at_10()
    {
        var a = _f.Customer("a@x.com");
        var b = _f.Customer("b@x.com");
        for (int i = 0; i < 12; i++) _f.Order(a, at: OrderFixture.Now.AddMinutes(i));
        _f.Order(a, OrderStatus.Confirmed);
        _f.Order(b);
        var tool = new ListMyOrdersTool(_f.ServiceFor(a));

        var all = (MyOrdersResult)await tool.ExecuteAsync(Json("{}"), Ct);
        all.TotalCount.ShouldBe(13);
        all.Orders.Count.ShouldBe(10);

        var confirmed = (MyOrdersResult)await tool.ExecuteAsync(Json("""{"status":"Confirmed"}"""), Ct);
        confirmed.TotalCount.ShouldBe(1);
        confirmed.Orders.Single().Status.ShouldBe(OrderStatus.Confirmed);
    }

    [Fact]
    public async Task ListMyOrders_with_no_orders_returns_empty()
    {
        var a = _f.Customer("a@x.com");
        var result = (MyOrdersResult)await new ListMyOrdersTool(_f.ServiceFor(a)).ExecuteAsync(Json("{}"), Ct);
        result.Orders.ShouldBeEmpty();
        result.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task ListMyOrders_rejects_unknown_property() =>
        await Should.ThrowAsync<ToolInputException>(() =>
            new ListMyOrdersTool(_f.ServiceFor(_f.Customer("a@x.com"))).ExecuteAsync(Json("""{"customerId":"x"}"""), Ct));

    [Fact]
    public void Schemas_forbid_additional_properties()
    {
        var a = _f.Customer("a@x.com");
        IAssistantTool[] tools = [new GetOrderStatusTool(_f.ServiceFor(a)), new ListMyOrdersTool(_f.ServiceFor(a))];
        tools.Select(t => t.Name).ShouldBe(["get_order_status", "list_my_orders"]);
        foreach (var t in tools)
        {
            t.InputSchema.GetProperty("type").GetString().ShouldBe("object");
            t.InputSchema.GetProperty("additionalProperties").GetBoolean().ShouldBeFalse();
            t.Description.ShouldContain("signed-in customer");
        }
        tools[0].InputSchema.GetProperty("required")[0].GetString().ShouldBe("orderNumber");
    }
}
