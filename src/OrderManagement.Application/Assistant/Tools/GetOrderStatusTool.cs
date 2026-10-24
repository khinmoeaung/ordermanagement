using System.Text.Json;
using System.Text.RegularExpressions;
using OrderManagement.Application.Orders;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Assistant.Tools;

public record OrderStatusItem(string ProductName, int Quantity);

public record OrderStatusResult(
    string OrderNumber, OrderStatus Status, DateTime CreatedAt, DateTime UpdatedAt,
    decimal TotalAmount, IReadOnlyList<OrderStatusItem> Items);

public sealed partial class GetOrderStatusTool(IOrderService orders) : IAssistantTool
{
    private const string OrderNumberPattern = @"^ORD-\d{8}-[A-Z0-9]{6}$";

    public string Name => "get_order_status";

    public string Description =>
        "Looks up one of the signed-in customer's own orders by its order number (for example ORD-20261023-AB12CD). " +
        "Returns the order number, status, created and updated times, total amount and the product names and quantities. " +
        "Reports 'Order not found.' when the order does not exist on the customer's account.";

    public JsonElement InputSchema { get; } = ToolInput.Schema(new
    {
        type = "object",
        properties = new
        {
            orderNumber = new { type = "string", pattern = OrderNumberPattern, description = "The order number, for example ORD-20261023-AB12CD." },
        },
        required = new[] { "orderNumber" },
        additionalProperties = false,
    });

    public async Task<object> ExecuteAsync(JsonElement input, CancellationToken ct)
    {
        ToolInput.RequireObject(input, "orderNumber");
        string orderNumber = ToolInput.RequiredString(input, "orderNumber").Trim().ToUpperInvariant();
        if (!OrderNumberRegex().IsMatch(orderNumber))
            throw new ToolInputException("'orderNumber' must look like ORD-20261023-AB12CD.");

        OrderResponse order = await orders.GetByNumberAsync(orderNumber, ct);
        return new OrderStatusResult(
            order.OrderNumber, order.Status, order.CreatedAt, order.UpdatedAt, order.TotalAmount,
            order.Items.Select(i => new OrderStatusItem(i.ProductName, i.Quantity)).ToList());
    }

    [GeneratedRegex(OrderNumberPattern)]
    private static partial Regex OrderNumberRegex();
}
