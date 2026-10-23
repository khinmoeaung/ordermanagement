using OrderManagement.Application.Common;
using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Orders;

public record OrderListQuery : PageQuery
{
    public OrderStatus? Status { get; init; }
    public Guid? CustomerId { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
}

public record PlaceOrderItem(Guid ProductId, int Quantity);

public record PlaceOrderRequest(IReadOnlyList<PlaceOrderItem> Items);

public record OrderItemResponse(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);

public record OrderResponse(
    Guid Id, string OrderNumber, Guid CustomerId, OrderStatus Status, decimal TotalAmount,
    DateTime CreatedAt, DateTime UpdatedAt, IReadOnlyList<OrderItemResponse> Items);

public interface IOrderRepository
{
    /// <summary>Includes OrderItems.</summary>
    Task<Order?> GetByIdAsync(Guid id, CancellationToken ct);
    void Add(Order o);

    /// <summary>Newest first. Filters by customerId (when set), Status, From and To (inclusive, on CreatedAt).</summary>
    Task<PagedResult<Order>> ListAsync(OrderListQuery q, Guid? customerId, CancellationToken ct);
}

public interface IOrderService
{
    Task<OrderResponse> PlaceAsync(PlaceOrderRequest r, CancellationToken ct);
    Task<OrderResponse> GetAsync(Guid id, CancellationToken ct);
    Task<PagedResult<OrderResponse>> ListMineAsync(OrderListQuery q, CancellationToken ct);
    Task<PagedResult<OrderResponse>> ListAsync(OrderListQuery q, CancellationToken ct);
    Task<OrderResponse> ConfirmAsync(Guid id, CancellationToken ct);
    Task<OrderResponse> ShipAsync(Guid id, CancellationToken ct);
    Task<OrderResponse> DeliverAsync(Guid id, CancellationToken ct);
    Task<OrderResponse> CompleteAsync(Guid id, CancellationToken ct);
    Task<OrderResponse> CancelAsync(Guid id, CancellationToken ct);
}
