using FluentValidation;
using FluentValidation.Results;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace OrderManagement.Api.Infrastructure;

/// <summary>
/// Runs every registered FluentValidation validator for each non-null action argument before the action.
/// Any failure short-circuits with a 400 <see cref="ValidationProblemDetails"/>, errors grouped by property.
/// </summary>
public sealed class ValidationFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        IServiceProvider services = context.HttpContext.RequestServices;
        CancellationToken ct = context.HttpContext.RequestAborted;
        List<ValidationFailure> failures = [];

        foreach (object? argument in context.ActionArguments.Values)
        {
            if (argument is null)
                continue;

            Type validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            foreach (IValidator validator in services.GetServices(validatorType).OfType<IValidator>())
            {
                ValidationResult result = await validator.ValidateAsync(new ValidationContext<object>(argument), ct);
                failures.AddRange(result.Errors);
            }
        }

        if (failures.Count == 0)
        {
            await next();
            return;
        }

        ModelStateDictionary modelState = new();
        foreach (ValidationFailure failure in failures)
            modelState.AddModelError(failure.PropertyName, failure.ErrorMessage);

        ProblemDetailsFactory factory = services.GetRequiredService<ProblemDetailsFactory>();
        ValidationProblemDetails problem = factory.CreateValidationProblemDetails(
            context.HttpContext, modelState, StatusCodes.Status400BadRequest);
        context.Result = new BadRequestObjectResult(problem);
    }
}
