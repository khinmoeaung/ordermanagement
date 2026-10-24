using FluentValidation;

namespace OrderManagement.Application.Assistant;

public sealed class ChatRequestValidator : AbstractValidator<ChatRequest>
{
    public ChatRequestValidator()
    {
        RuleFor(x => x.Message).NotEmpty().MaximumLength(2000);
    }
}
