using FluentValidation;

namespace OrderManagement.Application.Policies;

public sealed class AskPolicyRequestValidator : AbstractValidator<AskPolicyRequest>
{
    public AskPolicyRequestValidator()
    {
        RuleFor(x => x.Question).NotEmpty().MaximumLength(1000);
    }
}
