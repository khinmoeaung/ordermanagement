using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using OrderManagement.Api.Infrastructure;
using OrderManagement.Application.Policies;

namespace OrderManagement.Api.Controllers;

[ApiController]
[Route("api/v1/policies")]
public sealed class PoliciesController(IPolicyQuestionService questions, IPolicyIndexer indexer) : ControllerBase
{
    /// <summary>Answers a question about the shop's cancellation, refund and shipping policies, with source citations. Policy text only: it never reads order data.</summary>
    [Authorize]
    [HttpPost("ask")]
    [ProducesResponseType<PolicyAnswer>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PolicyAnswer>> Ask(AskPolicyRequest request, CancellationToken ct) =>
        Ok(await questions.AskAsync(request.Question, ct));

    /// <summary>Re-reads the policy files and brings the search index in line with them. Only new or changed chunks are embedded.</summary>
    [Authorize(AuthPolicies.AdminOnly)]
    [HttpPost("reindex")]
    [ProducesResponseType<ReindexResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ReindexResult>> Reindex(CancellationToken ct) =>
        Ok(await indexer.ReindexAsync(ct));
}
