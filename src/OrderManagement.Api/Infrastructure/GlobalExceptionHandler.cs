using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

using OrderManagement.Application.Assistant;
using OrderManagement.Application.Common;
using OrderManagement.Domain.Common;

namespace OrderManagement.Api.Infrastructure;

/// <summary>Turns unhandled exceptions into ProblemDetails responses (spec §6).</summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    private const string UnexpectedError = "An unexpected error occurred.";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        (int status, string detail) = exception switch
        {
            AuthenticationFailedException => (StatusCodes.Status401Unauthorized, exception.Message),
            AssistantNotConfiguredException => (StatusCodes.Status503ServiceUnavailable, "Assistant is not configured."),
            AssistantUnavailableException => (StatusCodes.Status503ServiceUnavailable, "Assistant is temporarily unavailable."),
            NotFoundException => (StatusCodes.Status404NotFound, exception.Message),
            ConflictException => (StatusCodes.Status409Conflict, exception.Message),
            DomainException => (StatusCodes.Status400BadRequest, exception.Message),
            _ => (StatusCodes.Status500InternalServerError, UnexpectedError)
        };

        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails { Status = status, Detail = detail }
        });
    }
}
