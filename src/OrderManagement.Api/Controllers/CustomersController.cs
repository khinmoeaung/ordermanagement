using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using OrderManagement.Api.Infrastructure;
using OrderManagement.Application.Common;
using OrderManagement.Application.Customers;

namespace OrderManagement.Api.Controllers;

[ApiController]
[Route("api/v1/customers")]
public sealed class CustomersController(ICustomerService customerService) : ControllerBase
{
    /// <summary>The caller's own profile.</summary>
    [Authorize(AuthPolicies.CustomerOnly)]
    [HttpGet("me")]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CustomerResponse>> GetMe(CancellationToken ct) =>
        Ok(await customerService.GetMeAsync(ct));

    /// <summary>Updates the caller's name, phone and shipping address.</summary>
    [Authorize(AuthPolicies.CustomerOnly)]
    [HttpPut("me")]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CustomerResponse>> UpdateMe(UpdateCustomerRequest request, CancellationToken ct) =>
        Ok(await customerService.UpdateMeAsync(request, ct));

    [Authorize(AuthPolicies.AdminOnly)]
    [HttpGet]
    [ProducesResponseType<PagedResult<CustomerResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<CustomerResponse>>> List(
        [FromQuery] CustomerListQuery query, CancellationToken ct) =>
        Ok(await customerService.ListAsync(query, ct));

    [Authorize(AuthPolicies.AdminOnly)]
    [HttpGet("{id:guid}")]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerResponse>> Get(Guid id, CancellationToken ct) =>
        Ok(await customerService.GetAsync(id, ct));
}
