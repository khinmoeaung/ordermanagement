using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OrderManagement.Application.Assistant;
using OrderManagement.Infrastructure.Assistant;
using Shouldly;
using static OrderManagement.UnitTests.Infrastructure.StubApiServer;

namespace OrderManagement.UnitTests.Infrastructure;

/// <summary>
/// Drives the real provider SDKs (through <see cref="ChatClientFactory"/> and <see cref="ChatClientAssistantModel"/>)
/// against a local stub server. These show what the real adapters do with tool calls, reasoning and errors.
/// </summary>
public sealed class ProviderRoundTripTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly StubApiServer _server = new();

    public void Dispose() => _server.Dispose();

    private static ChatClientAssistantModel ModelFor(AssistantOptions options, params (string Key, string? Value)[] settings)
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => s.Value))
            .Build();
        var factory = new ChatClientFactory(Options.Create(options), config);
        return new ChatClientAssistantModel(factory.Create, options.MaxTokens);
    }

    private ChatClientAssistantModel Claude() =>
        ModelFor(new AssistantOptions(), ("ANTHROPIC_API_KEY", "test-key"), ("ANTHROPIC_BASE_URL", _server.BaseUrl));

    private ChatClientAssistantModel OpenAi()
    {
        var options = new AssistantOptions { Provider = AssistantProvider.OpenAI };
        options.OpenAI.Endpoint = _server.BaseUrl;
        return ModelFor(options, ("OPENAI_API_KEY", "test-key"));
    }

    private static ChatClientAssistantModel OllamaAt(string endpoint)
    {
        var options = new AssistantOptions { Provider = AssistantProvider.Ollama };
        options.Ollama.Endpoint = endpoint;
        return ModelFor(options);
    }

    private static Task<(IAssistantConversation Conversation, ModelTurn FirstTurn)> Start(ChatClientAssistantModel model, CancellationToken? ct = null) =>
        model.StartAsync("system prompt", [new SchemaTool()], "hello", ct ?? Ct);

    private static string Order(string id) => $$"""{"orderNumber":"{{id}}"}""";

    // ---- Claude (Anthropic SDK through AsIChatClient) -------------------------------------------------

    [Fact]
    public async Task Claude_tool_turn_maps_to_tool_calls()
    {
        _server.Reply(Message("tool_use", ToolUse("tu_1", "get_order_status", Order("ORD-20260101-ABCDEF"))));
        var (_, turn) = await Start(Claude());

        turn.StopReason.ShouldBe(ModelStopReason.ToolUse);
        var call = turn.ToolCalls.ShouldHaveSingleItem();
        call.Id.ShouldBe("tu_1");
        call.Name.ShouldBe("get_order_status");
        call.Input.GetProperty("orderNumber").GetString().ShouldBe("ORD-20260101-ABCDEF");
    }

    [Fact]
    public async Task Claude_echoes_thinking_with_signature_and_sends_all_results_in_one_user_message()
    {
        _server.Reply(Message("tool_use", Thinking("", "sig-123"), ToolUse("tu_1", "a", "{}"), ToolUse("tu_2", "b", "{}")))
               .Reply(Message("end_turn", Text("done")));
        var (conversation, first) = await Start(Claude());
        first.ToolCalls.Count.ShouldBe(2);

        var turn = await conversation.ContinueAsync(
            [new ToolResult("tu_1", "{\"ok\":1}", false), new ToolResult("tu_2", "Order not found.", true)], Ct);
        turn.Text.ShouldBe("done");

        JsonElement messages = _server.Requests[1].GetProperty("messages");
        messages.GetArrayLength().ShouldBe(3);

        messages[1].GetProperty("role").GetString().ShouldBe("assistant");
        JsonElement echoed = messages[1].GetProperty("content");
        echoed.EnumerateArray().Select(b => b.GetProperty("type").GetString()).ShouldBe(["thinking", "tool_use", "tool_use"]);
        echoed[0].GetProperty("signature").GetString().ShouldBe("sig-123");

        messages[2].GetProperty("role").GetString().ShouldBe("user");
        JsonElement results = messages[2].GetProperty("content");
        results.GetArrayLength().ShouldBe(2);
        results.EnumerateArray().Select(b => b.GetProperty("type").GetString()).ShouldAllBe(t => t == "tool_result");
        results[0].GetProperty("tool_use_id").GetString().ShouldBe("tu_1");
        results[1].GetProperty("tool_use_id").GetString().ShouldBe("tu_2");
    }

    [Fact]
    public async Task Claude_server_error_after_retries_becomes_unavailable()
    {
        _server.FallbackStatus = 500;
        await Should.ThrowAsync<AssistantUnavailableException>(() => Start(Claude()));
        _server.Requests.Count.ShouldBe(3);   // first try + 2 SDK retries
    }

    [Fact]
    public async Task Claude_rate_limit_becomes_unavailable()
    {
        _server.FallbackStatus = 429;
        await Should.ThrowAsync<AssistantUnavailableException>(() => Start(Claude()));
    }

    [Fact]
    public async Task Claude_unauthorized_propagates_and_is_not_unavailable()
    {
        _server.FallbackStatus = 401;
        var ex = await Should.ThrowAsync<Exception>(() => Start(Claude()));
        ex.ShouldNotBeOfType<AssistantUnavailableException>();
        _server.Requests.Count.ShouldBe(1);   // a 401 is not retried
    }

    [Fact]
    public async Task Claude_caller_cancellation_is_not_unavailable()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ex = await Should.ThrowAsync<Exception>(() => Start(Claude(), cts.Token));
        ex.ShouldBeAssignableTo<OperationCanceledException>();
    }

    // ---- OpenAI ----------------------------------------------------------------------------------------

    private static string OpenAiToolCall(string callId, string name, string argumentsJson) => $$$"""
        {"id":"chatcmpl-1","object":"chat.completion","created":1,"model":"m",
         "choices":[{"index":0,"finish_reason":"tool_calls","message":{"role":"assistant","content":null,
           "tool_calls":[{"id":"{{{callId}}}","type":"function","function":{"name":"{{{name}}}","arguments":{{{JsonSerializer.Serialize(argumentsJson)}}}}}]}}],
         "usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}
        """;

    private static string OpenAiText(string text) => $$$"""
        {"id":"chatcmpl-2","object":"chat.completion","created":1,"model":"m",
         "choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":{{{JsonSerializer.Serialize(text)}}}}}],
         "usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}
        """;

    [Fact]
    public async Task OpenAI_tool_call_maps_to_ToolUse_and_result_is_sent_back()
    {
        _server.Reply(OpenAiToolCall("call_1", "get_order_status", Order("ORD-20260101-ABCDEF"))).Reply(OpenAiText("all good"));
        var (conversation, first) = await Start(OpenAi());

        first.StopReason.ShouldBe(ModelStopReason.ToolUse);
        var call = first.ToolCalls.ShouldHaveSingleItem();
        call.Id.ShouldBe("call_1");
        call.Input.GetProperty("orderNumber").GetString().ShouldBe("ORD-20260101-ABCDEF");

        var turn = await conversation.ContinueAsync([new ToolResult("call_1", "Order not found.", true)], Ct);
        turn.StopReason.ShouldBe(ModelStopReason.EndTurn);
        turn.Text.ShouldBe("all good");

        JsonElement tool = _server.Requests[1].GetProperty("messages").EnumerateArray()
            .Single(m => m.GetProperty("role").GetString() == "tool");
        tool.GetProperty("tool_call_id").GetString().ShouldBe("call_1");
        tool.ToString().ShouldContain("Order not found.");
    }

    [Fact]
    public async Task OpenAI_server_error_becomes_unavailable()
    {
        _server.FallbackStatus = 500;
        await Should.ThrowAsync<AssistantUnavailableException>(() => Start(OpenAi()));
    }

    // ---- Ollama ----------------------------------------------------------------------------------------

    private const string OllamaToolCall =
        """{"model":"qwen2.5:7b","created_at":"2026-01-01T00:00:00Z","message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"get_order_status","arguments":{"orderNumber":"ORD-20260101-ABCDEF"}}}]},"done":true,"done_reason":"stop"}""";

    private const string OllamaDone =
        """{"model":"qwen2.5:7b","created_at":"2026-01-01T00:00:01Z","message":{"role":"assistant","content":"all good"},"done":true,"done_reason":"stop"}""";

    [Fact]
    public async Task Ollama_tool_call_maps_to_ToolUse_and_result_is_sent_back()
    {
        _server.Reply(OllamaToolCall).Reply(OllamaDone);
        var (conversation, first) = await Start(OllamaAt(_server.BaseUrl));

        first.StopReason.ShouldBe(ModelStopReason.ToolUse);
        var call = first.ToolCalls.ShouldHaveSingleItem();
        call.Id.ShouldNotBeNullOrWhiteSpace();   // Ollama sends no ID; the adapter supplies one
        call.Input.GetProperty("orderNumber").GetString().ShouldBe("ORD-20260101-ABCDEF");

        var turn = await conversation.ContinueAsync([new ToolResult(call.Id, "Order not found.", true)], Ct);
        turn.Text.ShouldBe("all good");

        _server.Requests[1].GetProperty("messages").EnumerateArray()
            .ShouldContain(m => m.GetProperty("role").GetString() == "tool" && m.ToString().Contains("Order not found."));
    }

    [Fact]
    public async Task Ollama_unreachable_becomes_unavailable()
    {
        // A port nothing listens on: the laptop is off or unreachable.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int closedPort = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        await Should.ThrowAsync<AssistantUnavailableException>(() => Start(OllamaAt($"http://127.0.0.1:{closedPort}")));
    }

    private sealed class SchemaTool : IAssistantTool
    {
        public string Name => "get_order_status";
        public string Description => "Looks up the customer's order.";
        public JsonElement InputSchema => JsonDocument.Parse("""
            {"type":"object","properties":{"orderNumber":{"type":"string"}},"required":["orderNumber"],"additionalProperties":false}
            """).RootElement;
        public Task<object> ExecuteAsync(JsonElement input, CancellationToken ct) => Task.FromResult<object>(1);
    }
}
