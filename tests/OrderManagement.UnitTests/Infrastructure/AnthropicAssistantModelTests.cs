using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OrderManagement.Application.Assistant;
using OrderManagement.Infrastructure.Assistant;
using Shouldly;
using static OrderManagement.UnitTests.Infrastructure.StubAnthropicServer;

namespace OrderManagement.UnitTests.Infrastructure;

public sealed class AnthropicAssistantModelTests : IDisposable
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private readonly StubAnthropicServer _server = new();

    public void Dispose() => _server.Dispose();

    private AnthropicAssistantModel Sut(string apiKey = "test-key", bool fallbacks = true) =>
        new(Options.Create(new AssistantOptions { EnableFallbacks = fallbacks }),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ANTHROPIC_API_KEY"] = apiKey,
                ["ANTHROPIC_BASE_URL"] = _server.BaseUrl,
            }).Build());

    private static IAssistantTool[] Tools => [new SchemaTool()];

    [Fact]
    public async Task Start_without_key_throws_not_configured()
    {
        await Should.ThrowAsync<AssistantNotConfiguredException>(() => Sut(apiKey: "").StartAsync("sys", Tools, "hi", Ct));
        _server.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Maps_end_turn_text()
    {
        _server.Reply(Message("end_turn", Text("Hello"), Text("there")));
        var (_, turn) = await Sut().StartAsync("sys", Tools, "hi", Ct);

        turn.StopReason.ShouldBe(ModelStopReason.EndTurn);
        turn.Text.ShouldBe("Hello\nthere");
        turn.ToolCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Maps_tool_use_turn_to_tool_calls()
    {
        _server.Reply(Message("tool_use",
            ToolUse("tu_1", "get_order_status", """{"orderNumber":"ORD-20260101-ABCDEF"}"""),
            ToolUse("tu_2", "list_my_orders", "{}")));
        var (_, turn) = await Sut().StartAsync("sys", Tools, "hi", Ct);

        turn.StopReason.ShouldBe(ModelStopReason.ToolUse);
        turn.ToolCalls.Select(c => (c.Id, c.Name)).ShouldBe([("tu_1", "get_order_status"), ("tu_2", "list_my_orders")]);
        turn.ToolCalls[0].Input.GetProperty("orderNumber").GetString().ShouldBe("ORD-20260101-ABCDEF");
    }

    [Theory]
    [InlineData("refusal", ModelStopReason.Refusal)]
    [InlineData("max_tokens", ModelStopReason.MaxTokens)]
    [InlineData("pause_turn", ModelStopReason.Other)]
    public async Task Maps_other_stop_reasons(string wire, ModelStopReason expected)
    {
        _server.Reply(Message(wire, Text("x")));
        var (_, turn) = await Sut().StartAsync("sys", Tools, "hi", Ct);
        turn.StopReason.ShouldBe(expected);
    }

    [Fact]
    public async Task Continue_echoes_thinking_with_signature_and_sends_all_results_in_one_user_message()
    {
        _server.Reply(Message("tool_use", Thinking("", "sig-123"), ToolUse("tu_1", "a", "{}"), ToolUse("tu_2", "b", "{}")))
               .Reply(Message("end_turn", Text("done")));
        var (conversation, _) = await Sut().StartAsync("sys", Tools, "hi", Ct);

        var turn = await conversation.ContinueAsync([new ToolResult("tu_1", "{\"ok\":1}", false), new ToolResult("tu_2", "Order not found.", true)], Ct);
        turn.Text.ShouldBe("done");

        JsonElement messages = _server.Requests[1].GetProperty("messages");
        messages.GetArrayLength().ShouldBe(3);
        messages[1].GetProperty("role").GetString().ShouldBe("assistant");
        JsonElement echoed = messages[1].GetProperty("content");
        echoed[0].GetProperty("type").GetString().ShouldBe("thinking");
        echoed[0].GetProperty("signature").GetString().ShouldBe("sig-123");
        echoed[1].GetProperty("type").GetString().ShouldBe("tool_use");

        messages[2].GetProperty("role").GetString().ShouldBe("user");
        JsonElement results = messages[2].GetProperty("content");
        results.GetArrayLength().ShouldBe(2);
        results[0].GetProperty("tool_use_id").GetString().ShouldBe("tu_1");
        results[1].GetProperty("tool_use_id").GetString().ShouldBe("tu_2");
        results[1].GetProperty("is_error").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Request_uses_options_strict_tools_and_fallbacks()
    {
        _server.Reply(Message("end_turn", Text("ok")));
        await Sut().StartAsync("the system prompt", Tools, "hi", Ct);

        JsonElement body = _server.Requests.Single();
        body.GetProperty("model").GetString().ShouldBe("claude-opus-5-5");
        body.GetProperty("max_tokens").GetInt32().ShouldBe(16000);
        body.GetProperty("system").ToString().ShouldContain("the system prompt");
        body.GetProperty("output_config").GetProperty("effort").GetString().ShouldBe("low");
        body.GetProperty("tool_choice").GetProperty("type").GetString().ShouldBe("auto");
        body.TryGetProperty("thinking", out _).ShouldBeFalse();
        body.GetProperty("fallbacks").GetString().ShouldBe("default");

        JsonElement tool = body.GetProperty("tools")[0];
        tool.GetProperty("name").GetString().ShouldBe("get_order_status");
        tool.GetProperty("strict").GetBoolean().ShouldBeTrue();
        JsonElement schema = tool.GetProperty("input_schema");
        schema.GetProperty("additionalProperties").GetBoolean().ShouldBeFalse();
        schema.GetProperty("required")[0].GetString().ShouldBe("orderNumber");
        schema.GetProperty("properties").GetProperty("orderNumber").GetProperty("pattern").GetString().ShouldBe("^ORD-$");
    }

    [Fact]
    public async Task Fallbacks_omitted_when_disabled()
    {
        _server.Reply(Message("end_turn", Text("ok")));
        await Sut(fallbacks: false).StartAsync("sys", Tools, "hi", Ct);
        _server.Requests.Single().TryGetProperty("fallbacks", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Server_error_after_retries_throws_unavailable()
    {
        _server.FallbackStatus = 500;
        await Should.ThrowAsync<AssistantUnavailableException>(() => Sut().StartAsync("sys", Tools, "hi", Ct));
        _server.Requests.Count.ShouldBe(3);   // first try + 2 SDK retries
    }

    [Theory]
    [InlineData(0, 16000, "low")]
    [InlineData(11, 16000, "low")]
    [InlineData(5, 0, "low")]
    [InlineData(5, 16000, "turbo")]
    public void Options_validation_rejects_out_of_range_values(int rounds, int maxTokens, string effort) =>
        AssistantOptions.Validate(new AssistantOptions { MaxToolRounds = rounds, MaxTokens = maxTokens, Effort = effort }).ShouldBeFalse();

    [Fact]
    public void Options_defaults_are_valid() => AssistantOptions.Validate(new AssistantOptions()).ShouldBeTrue();

    private sealed class SchemaTool : IAssistantTool
    {
        public string Name => "get_order_status";
        public string Description => "desc";
        public JsonElement InputSchema => JsonDocument.Parse("""
            {"type":"object","properties":{"orderNumber":{"type":"string","pattern":"^ORD-$"}},
             "required":["orderNumber"],"additionalProperties":false}
            """).RootElement;
        public Task<object> ExecuteAsync(JsonElement input, CancellationToken ct) => Task.FromResult<object>(1);
    }
}
