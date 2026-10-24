using FluentValidation.TestHelper;
using OrderManagement.Application.Policies;

namespace OrderManagement.UnitTests.Application.Policies;

public class AskPolicyRequestValidatorTests
{
    private readonly AskPolicyRequestValidator _v = new();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_or_whitespace_question_rejected(string q) =>
        _v.TestValidate(new AskPolicyRequest(q)).ShouldHaveValidationErrorFor(x => x.Question);

    [Fact]
    public void Length_limit_is_1000_characters()
    {
        _v.TestValidate(new AskPolicyRequest(new string('a', 1000))).ShouldNotHaveAnyValidationErrors();
        _v.TestValidate(new AskPolicyRequest(new string('a', 1001))).ShouldHaveValidationErrorFor(x => x.Question);
    }
}
