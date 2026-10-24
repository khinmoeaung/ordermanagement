using System.Text.Json;
using OrderManagement.Application.Assistant;
using OrderManagement.Application.Assistant.Tools;
using OrderManagement.UnitTests.Fakes;
using Shouldly;
using static OrderManagement.UnitTests.Fakes.FakeAssistantModel;

namespace OrderManagement.UnitTests.Application.Assistant;

public class AssistantServiceTests
{
    private const string Fallback = "Sorry, I couldn't complete that request. Please try again.";
    private static readonly CancellationToken Ct = CancellationToken.None;

    private readonly FakeAssistantModel _model = new();
    private readonly OrderFixture _f = new();

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private AssistantService Sut(int maxToolRounds = 5)
    {
        var service = _f.ServiceFor(_f.Customer("a@x.com"));
        var executor = new ToolExecutor([new GetOrderStatusTool(service), new ListMyOrdersTool(service)], new RecordingLogger<ToolExecutor>());
        return new AssistantService(_model, executor, maxToolRounds);
    }

    private static ToolCall ListCall(string id) => new(id, "list_my_orders", Json("{}"));

    private Task<ChatResponse> Ask(AssistantService sut) => sut.ChatAsync(new ChatRequest("What are my orders?"), Ct);

    [Fact]
    public async Task Direct_answer_returns_text()
    {
        _model.Then(Answer(" hi "));
        (await Ask(Sut())).Reply.ShouldBe("hi");
        _model.ResultBatches.ShouldBeEmpty();
    }

    [Fact]
    public async Task One_tool_round_then_answer()
    {
        _model.Then(Calls(ListCall("c1"))).Then(Answer("You have no orders."));
        (await Ask(Sut())).Reply.ShouldBe("You have no orders.");

        var batch = _model.ResultBatches.ShouldHaveSingleItem();
        batch.ShouldHaveSingleItem().CallId.ShouldBe("c1");
        batch[0].IsError.ShouldBeFalse();
        batch[0].Content.ShouldContain("\"totalCount\":0");
    }

    [Fact]
    public async Task Two_calls_in_one_turn_are_returned_as_one_batch_in_order()
    {
        _model.Then(Calls(ListCall("c1"), new ToolCall("c2", "nope", Json("{}")))).Then(Answer("done"));
        await Ask(Sut());

        var batch = _model.ResultBatches.ShouldHaveSingleItem();
        batch.Select(r => r.CallId).ShouldBe(["c1", "c2"]);
        batch[1].Content.ShouldBe("Unknown tool.");
    }

    [Fact]
    public async Task Exceeding_round_limit_returns_fallback()
    {
        for (int i = 0; i < 6; i++) _model.Then(Calls(ListCall("c" + i)));
        (await Ask(Sut(maxToolRounds: 5))).Reply.ShouldBe(Fallback);
        _model.ResultBatches.Count.ShouldBe(5);
    }

    [Fact]
    public async Task Refusal_returns_refusal_text()
    {
        _model.Then(new ModelTurn(ModelStopReason.Refusal, "", []));
        (await Ask(Sut())).Reply.ShouldBe("Sorry, I can't help with that request.");
    }

    [Theory]
    [InlineData(ModelStopReason.EndTurn, "   ")]
    [InlineData(ModelStopReason.MaxTokens, "partial")]
    [InlineData(ModelStopReason.Other, "x")]
    public async Task Empty_text_or_max_tokens_returns_fallback(ModelStopReason reason, string text)
    {
        _model.Then(new ModelTurn(reason, text, []));
        (await Ask(Sut())).Reply.ShouldBe(Fallback);
    }

    [Fact]
    public async Task Tool_use_turn_with_no_calls_returns_fallback()
    {
        _model.Then(new ModelTurn(ModelStopReason.ToolUse, "", []));
        (await Ask(Sut())).Reply.ShouldBe(Fallback);
        _model.ResultBatches.ShouldBeEmpty();
    }

    [Fact]
    public async Task Calls_beyond_ten_per_turn_are_not_executed()
    {
        var calls = Enumerable.Range(1, 12).Select(i => ListCall("c" + i)).ToArray();
        _model.Then(Calls(calls)).Then(Answer("done"));
        await Ask(Sut());

        var batch = _model.ResultBatches.ShouldHaveSingleItem();
        batch.Count.ShouldBe(12);   // every tool_use still gets a result
        batch.Take(10).ShouldAllBe(r => !r.IsError);
        batch.Skip(10).ShouldAllBe(r => r.IsError && r.Content == "Too many tool calls.");
        batch.Select(r => r.CallId).ShouldBe(calls.Select(c => c.Id));
    }

    [Fact]
    public async Task Sends_fixed_prompt_and_registered_tools()
    {
        _model.Then(Answer("ok"));
        await Ask(Sut());

        _model.SystemPrompt.ShouldBe(AssistantPrompt.System);
        _model.ToolNames.ShouldBe(["get_order_status", "list_my_orders"]);
        _model.UserMessage.ShouldBe("What are my orders?");
    }
}
