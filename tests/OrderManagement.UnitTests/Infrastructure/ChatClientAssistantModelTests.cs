using System.Net;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OrderManagement.Application.Assistant;
using AiChatResponse = Microsoft.Extensions.AI.ChatResponse;
using OrderManagement.Infrastructure.Assistant;
using OrderManagement.UnitTests.Fakes;
using Shouldly;

namespace OrderManagement.UnitTests.Infrastructure;

public class ChatClientAssistantModelTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private readonly ScriptedChatClient _client = new();

    private ChatClientAssistantModel Sut(int maxTokens = 4096) => new(() => _client, maxTokens);

    private static FunctionCallContent Call(string id, string name, IDictionary<string, object?>? args) => new(id, name, args);

    private Task<(IAssistantConversation Conversation, ModelTurn FirstTurn)> Start(ChatClientAssistantModel? sut = null) =>
        (sut ?? Sut()).StartAsync("the prompt", [new StubTool("get_order_status"), new StubTool("list_my_orders")], "hello", Ct);

    [Fact]
    public async Task Start_without_client_throws_not_configured() =>
        await Should.ThrowAsync<AssistantNotConfiguredException>(() =>
            new ChatClientAssistantModel(() => null, 4096).StartAsync("p", [], "m", Ct));

    [Fact]
    public async Task Client_factory_is_called_once_across_conversations()
    {
        int created = 0;
        var sut = new ChatClientAssistantModel(() => { created++; return _client; }, 4096);
        _client.Reply(ScriptedChatClient.Text("a")).Reply(ScriptedChatClient.Text("b"));

        await Start(sut);
        await Start(sut);

        created.ShouldBe(1);
    }

    [Fact]
    public async Task First_request_has_system_and_user_messages_and_declaration_only_tools()
    {
        _client.Reply(ScriptedChatClient.Text("hi"));
        await Start();

        var messages = _client.Calls.ShouldHaveSingleItem();
        messages.Select(m => m.Role).ShouldBe([ChatRole.System, ChatRole.User]);
        messages[0].Text.ShouldBe("the prompt");
        messages[1].Text.ShouldBe("hello");

        var options = _client.Options.ShouldHaveSingleItem()!;
        options.MaxOutputTokens.ShouldBe(4096);
        options.ToolMode.ShouldBeOfType<AutoChatToolMode>();
        options.Tools!.Select(t => t.Name).ShouldBe(["get_order_status", "list_my_orders"]);
        foreach (AITool tool in options.Tools!)
        {
            tool.ShouldBeAssignableTo<AIFunctionDeclaration>();
            tool.ShouldNotBeAssignableTo<AIFunction>();   // declaration only: nothing can invoke it
        }
    }

    [Fact]
    public async Task Text_response_maps_to_EndTurn_and_joins_parts_with_newline()
    {
        _client.Reply(new AiChatResponse(new ChatMessage(ChatRole.Assistant, [new TextContent("one"), new TextContent(""), new TextContent("two")]))
        {
            FinishReason = ChatFinishReason.Stop,
        });
        var (_, turn) = await Start();

        turn.StopReason.ShouldBe(ModelStopReason.EndTurn);
        turn.Text.ShouldBe("one\ntwo");
        turn.ToolCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Tool_call_maps_to_ToolUse_even_when_finish_reason_is_Stop()
    {
        _client.Reply(ScriptedChatClient.CallsTool(ChatFinishReason.Stop,
            Call("c1", "get_order_status", new Dictionary<string, object?> { ["orderNumber"] = "ORD-20260101-ABCDEF" })));
        var (_, turn) = await Start();

        turn.StopReason.ShouldBe(ModelStopReason.ToolUse);
        var call = turn.ToolCalls.ShouldHaveSingleItem();
        call.Name.ShouldBe("get_order_status");
        call.Input.GetProperty("orderNumber").GetString().ShouldBe("ORD-20260101-ABCDEF");
    }

    [Theory]
    [InlineData("length", ModelStopReason.MaxTokens)]
    [InlineData("content_filter", ModelStopReason.Refusal)]
    [InlineData("tool_calls", ModelStopReason.Other)]   // claims tool calls but sent none
    public async Task Other_finish_reasons_map(string finish, ModelStopReason expected)
    {
        _client.Reply(ScriptedChatClient.Text("x", new ChatFinishReason(finish)));
        var (_, turn) = await Start();
        turn.StopReason.ShouldBe(expected);
    }

    [Fact]
    public async Task Null_arguments_become_empty_object()
    {
        _client.Reply(ScriptedChatClient.CallsTool(null, Call("c1", "list_my_orders", null)));
        var (_, turn) = await Start();

        var input = turn.ToolCalls.ShouldHaveSingleItem().Input;
        input.ValueKind.ShouldBe(JsonValueKind.Object);
        input.GetRawText().ShouldBe("{}");
    }

    [Fact]
    public async Task Blank_call_ids_are_replaced_with_distinct_ids_and_results_use_them()
    {
        _client.Reply(ScriptedChatClient.CallsTool(null, Call(" ", "list_my_orders", null), Call("", "list_my_orders", null)))
               .Reply(ScriptedChatClient.Text("done"));
        var (conversation, turn) = await Start();

        string[] ids = turn.ToolCalls.Select(c => c.Id).ToArray();
        ids.ShouldAllBe(id => !string.IsNullOrWhiteSpace(id));
        ids.Distinct().Count().ShouldBe(2);

        await conversation.ContinueAsync([new ToolResult(ids[0], "r0", false), new ToolResult(ids[1], "r1", false)], Ct);

        var history = _client.Calls[1];
        history[2].Contents.OfType<FunctionCallContent>().Select(c => c.CallId).ShouldBe(ids);
        history[3].Contents.OfType<FunctionResultContent>().Select(r => r.CallId).ShouldBe(ids);
    }

    [Fact]
    public async Task Continue_keeps_assistant_message_and_sends_one_tool_message_in_order()
    {
        var assistant = ScriptedChatClient.CallsTool(ChatFinishReason.ToolCalls, Call("c1", "a", null), Call("c2", "b", null));
        var reasoning = new TextReasoningContent("thinking") { ProtectedData = "sig-123" };
        assistant.Messages[0].Contents.Insert(0, reasoning);
        _client.Reply(assistant).Reply(ScriptedChatClient.Text("final"));
        var (conversation, _) = await Start();

        var turn = await conversation.ContinueAsync([new ToolResult("c1", "first", false), new ToolResult("c2", "Order not found.", true)], Ct);

        turn.Text.ShouldBe("final");
        var history = _client.Calls[1];
        history.Select(m => m.Role).ShouldBe([ChatRole.System, ChatRole.User, ChatRole.Assistant, ChatRole.Tool]);
        history[2].ShouldBeSameAs(assistant.Messages[0]);
        history[2].Contents.OfType<TextReasoningContent>().Single().ProtectedData.ShouldBe("sig-123");
        var results = history[3].Contents.OfType<FunctionResultContent>().ToList();
        results.Select(r => r.CallId).ShouldBe(["c1", "c2"]);
        results.Select(r => r.Result?.ToString()).ShouldBe(["first", "Order not found."]);
    }

    [Fact]
    public async Task Transient_provider_error_becomes_unavailable()
    {
        _client.Throw(new HttpRequestException("down", null, HttpStatusCode.ServiceUnavailable));
        var ex = await Should.ThrowAsync<AssistantUnavailableException>(() => Start());
        ex.InnerException.ShouldBeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task Non_transient_provider_error_propagates()
    {
        _client.Throw(new HttpRequestException("bad key", null, HttpStatusCode.Unauthorized));
        await Should.ThrowAsync<HttpRequestException>(() => Start());
    }

    [Fact]
    public async Task Caller_cancellation_propagates_as_cancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _client.Throw(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() =>
            Sut().StartAsync("p", [new StubTool("t")], "m", cts.Token));
    }

    private sealed class StubTool(string name) : IAssistantTool
    {
        public string Name => name;
        public string Description => "desc";
        public JsonElement InputSchema => JsonDocument.Parse("""{"type":"object","properties":{},"additionalProperties":false}""").RootElement;
        public Task<object> ExecuteAsync(JsonElement input, CancellationToken ct) => Task.FromResult<object>(1);
    }
}
