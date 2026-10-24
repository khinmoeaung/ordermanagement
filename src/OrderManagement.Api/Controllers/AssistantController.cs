using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using OrderManagement.Api.Infrastructure;
using OrderManagement.Application.Assistant;

namespace OrderManagement.Api.Controllers;

[ApiController]
[Route("api/v1/assistant")]
public sealed class AssistantController(IAssistantService assistant) : ControllerBase
{
    /// <summary>Answers the signed-in customer's question about their own orders using Claude and read-only tools.</summary>
    [Authorize(AuthPolicies.CustomerOnly)]
    [HttpPost("chat")]
    [ProducesResponseType<ChatResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ChatResponse>> Chat(ChatRequest request, CancellationToken ct) =>
        Ok(await assistant.ChatAsync(request, ct));
}
