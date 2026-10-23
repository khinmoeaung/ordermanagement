using FluentValidation.TestHelper;
using OrderManagement.Application.Assistant;

namespace OrderManagement.UnitTests.Application.Assistant;

public class ChatRequestValidatorTests
{
    private readonly ChatRequestValidator _v = new();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_message_rejected(string m) =>
        _v.TestValidate(new ChatRequest(m)).ShouldHaveValidationErrorFor(x => x.Message);

    [Fact]
    public void Message_of_2000_accepted_and_2001_rejected()
    {
        _v.TestValidate(new ChatRequest(new string('a', 2000))).ShouldNotHaveAnyValidationErrors();
        _v.TestValidate(new ChatRequest(new string('a', 2001))).ShouldHaveValidationErrorFor(x => x.Message);
    }
}
