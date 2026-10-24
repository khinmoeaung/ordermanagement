using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using OrderManagement.Api.Infrastructure;
using OrderManagement.Application.Common;
using OrderManagement.Application.Orders;

namespace OrderManagement.Api.Controllers;

[ApiController]
[Route("api/v1/orders")]
public sealed class OrdersController(IOrderService orderService) : ControllerBase
{
    /// <summary>Places an order for the caller and deducts stock.</summary>
    [Authorize(AuthPolicies.CustomerOnly)]
    [HttpPost]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Place(PlaceOrderRequest request, CancellationToken ct)
    {
        OrderResponse body = await orderService.PlaceAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = body.Id }, body);
    }

    /// <summary>Lists the caller's own orders, newest first.</summary>
    [Authorize(AuthPolicies.CustomerOnly)]
    [HttpGet("my")]
    [ProducesResponseType<PagedResult<OrderResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<OrderResponse>>> ListMine(
        [FromQuery] OrderListQuery query, CancellationToken ct) =>
        Ok(await orderService.ListMineAsync(query, ct));

    /// <summary>Lists all orders, newest first, optionally filtered.</summary>
    [Authorize(AuthPolicies.AdminOnly)]
    [HttpGet]
    [ProducesResponseType<PagedResult<OrderResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<OrderResponse>>> List(
        [FromQuery] OrderListQuery query, CancellationToken ct) =>
        Ok(await orderService.ListAsync(query, ct));

    /// <summary>Gets an order. A customer only sees their own; others are 404.</summary>
    [Authorize]
    [HttpGet("{id:guid}")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderResponse>> Get(Guid id, CancellationToken ct) =>
        Ok(await orderService.GetAsync(id, ct));

    [Authorize(AuthPolicies.AdminOnly)]
    [HttpPost("{id:guid}/confirm")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderResponse>> Confirm(Guid id, CancellationToken ct) =>
        Ok(await orderService.ConfirmAsync(id, ct));

    [Authorize(AuthPolicies.AdminOnly)]
    [HttpPost("{id:guid}/ship")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderResponse>> Ship(Guid id, CancellationToken ct) =>
        Ok(await orderService.ShipAsync(id, ct));

    [Authorize(AuthPolicies.AdminOnly)]
    [HttpPost("{id:guid}/deliver")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderResponse>> Deliver(Guid id, CancellationToken ct) =>
        Ok(await orderService.DeliverAsync(id, ct));

    /// <summary>Completes a delivered order. The service checks ownership.</summary>
    [Authorize]
    [HttpPost("{id:guid}/complete")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderResponse>> Complete(Guid id, CancellationToken ct) =>
        Ok(await orderService.CompleteAsync(id, ct));

    /// <summary>Cancels a pending or confirmed order and restores stock. The service checks ownership.</summary>
    [Authorize]
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderResponse>> Cancel(Guid id, CancellationToken ct) =>
        Ok(await orderService.CancelAsync(id, ct));
}
