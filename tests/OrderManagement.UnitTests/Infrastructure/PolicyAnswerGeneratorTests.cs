using Microsoft.Extensions.AI;
using OrderManagement.Application.Assistant;
using OrderManagement.Infrastructure.Policies;
using OrderManagement.UnitTests.Fakes;
using Shouldly;

namespace OrderManagement.UnitTests.Infrastructure;

public class PolicyAnswerGeneratorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sends_system_and_user_messages_with_no_tools_and_returns_the_text()
    {
        var client = new ScriptedChatClient().Reply(ScriptedChatClient.Text("The answer [1]."));
        var generator = new PolicyAnswerGenerator(() => client, 777);

        string answer = await generator.GenerateAsync("system rules", "user message", Ct);

        answer.ShouldBe("The answer [1].");
        var messages = client.Calls.Single();
        messages.Select(m => m.Role).ShouldBe([ChatRole.System, ChatRole.User]);
        messages[0].Text.ShouldBe("system rules");
        messages[1].Text.ShouldBe("user message");
        var options = client.Options.Single();
        options.ShouldNotBeNull();
        options.MaxOutputTokens.ShouldBe(777);
        (options.Tools ?? []).ShouldBeEmpty();
    }

    [Fact]
    public async Task Missing_client_throws_assistant_not_configured()
    {
        var generator = new PolicyAnswerGenerator(() => null, 100);

        await Should.ThrowAsync<AssistantNotConfiguredException>(() => generator.GenerateAsync("s", "u", Ct));
    }

    [Fact]
    public async Task Transient_provider_error_becomes_assistant_unavailable()
    {
        var client = new ScriptedChatClient().Throw(new HttpRequestException("connection refused"));
        var generator = new PolicyAnswerGenerator(() => client, 100);

        await Should.ThrowAsync<AssistantUnavailableException>(() => generator.GenerateAsync("s", "u", Ct));
    }

    [Fact]
    public async Task Non_transient_provider_error_propagates()
    {
        var client = new ScriptedChatClient().Throw(new InvalidOperationException("bad request"));
        var generator = new PolicyAnswerGenerator(() => client, 100);

        await Should.ThrowAsync<InvalidOperationException>(() => generator.GenerateAsync("s", "u", Ct));
    }
}
