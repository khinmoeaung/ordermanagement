using System.Text.Json;
using OrderManagement.Application.Common;
using OrderManagement.Application.Orders;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Assistant.Tools;

public record MyOrderSummary(string OrderNumber, OrderStatus Status, DateTime CreatedAt, decimal TotalAmount);

public record MyOrdersResult(IReadOnlyList<MyOrderSummary> Orders, int TotalCount);

public sealed class ListMyOrdersTool(IOrderService orders) : IAssistantTool
{
    private const int MaxOrders = 10;

    public string Name => "list_my_orders";

    public string Description =>
        "Lists the signed-in customer's own most recent orders, newest first, at most 10. " +
        "Optionally filters by status. Returns each order's number, status, created time and total amount, plus the total count.";

    public JsonElement InputSchema { get; } = ToolInput.Schema(new
    {
        type = "object",
        properties = new
        {
            status = new { type = "string", @enum = Enum.GetNames<OrderStatus>(), description = "Only return orders in this status." },
        },
        additionalProperties = false,
    });

    public async Task<object> ExecuteAsync(JsonElement input, CancellationToken ct)
    {
        ToolInput.RequireObject(input, "status");
        OrderStatus? status = ToolInput.OptionalEnum<OrderStatus>(input, "status");

        PagedResult<OrderResponse> page = await orders.ListMineAsync(
            new OrderListQuery { Status = status, Page = 1, PageSize = MaxOrders }, ct);
        return new MyOrdersResult(
            page.Items.Select(o => new MyOrderSummary(o.OrderNumber, o.Status, o.CreatedAt, o.TotalAmount)).ToList(),
            page.TotalCount);
    }
}
