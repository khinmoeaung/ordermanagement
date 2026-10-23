using System.Text.Json;
using Microsoft.Extensions.Logging;
using OrderManagement.Application.Assistant;
using OrderManagement.Application.Assistant.Tools;
using OrderManagement.Domain.Common;
using OrderManagement.Domain.Enums;
using Shouldly;

namespace OrderManagement.UnitTests.Application.Assistant;

public class ToolExecutorTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private readonly RecordingLogger<ToolExecutor> _logger = new();

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private ToolExecutor Sut(params IAssistantTool[] tools) => new(tools, _logger);

    [Fact]
    public async Task Unknown_tool_is_not_executed()
    {
        var stub = new StubTool("get_order_status", _ => new { ok = true });
        var result = await Sut(stub).ExecuteAsync(new ToolCall("c1", "delete_order", Json("{}")), Ct);

        result.ShouldBe(new ToolResult("c1", "Unknown tool.", true));
        stub.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Tool_input_exception_becomes_invalid_arguments()
    {
        var stub = new StubTool("t", _ => throw new ToolInputException("'orderNumber' is required."));
        var result = await Sut(stub).ExecuteAsync(new ToolCall("c1", "t", Json("{}")), Ct);

        result.Content.ShouldBe("Invalid arguments: 'orderNumber' is required.");
        result.IsError.ShouldBeTrue();
    }

    [Fact]
    public async Task Not_found_becomes_order_not_found()
    {
        var stub = new StubTool("t", _ => throw new NotFoundException("Order", Guid.NewGuid()));
        var result = await Sut(stub).ExecuteAsync(new ToolCall("c1", "t", Json("{}")), Ct);

        result.ShouldBe(new ToolResult("c1", "Order not found.", true));
    }

    [Fact]
    public async Task Unexpected_exception_becomes_tool_failed_and_is_logged()
    {
        var stub = new StubTool("t", _ => throw new InvalidOperationException("db exploded: secret detail"));
        var result = await Sut(stub).ExecuteAsync(new ToolCall("c1", "t", Json("{}")), Ct);

        result.ShouldBe(new ToolResult("c1", "Tool failed.", true));
        _logger.Entries.ShouldContain(e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var stub = new StubTool("t", _ => throw new OperationCanceledException());
        await Should.ThrowAsync<OperationCanceledException>(() => Sut(stub).ExecuteAsync(new ToolCall("c1", "t", Json("{}")), Ct));
    }

    [Fact]
    public async Task Success_serializes_camelcase_with_string_enums()
    {
        var stub = new StubTool("t", _ => new MyOrderSummary("ORD-20260101-ABCDEF", OrderStatus.Pending, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 25m));
        var result = await Sut(stub).ExecuteAsync(new ToolCall("c9", "t", Json("{}")), Ct);

        result.CallId.ShouldBe("c9");
        result.IsError.ShouldBeFalse();
        var json = JsonDocument.Parse(result.Content).RootElement;
        json.GetProperty("orderNumber").GetString().ShouldBe("ORD-20260101-ABCDEF");
        json.GetProperty("status").GetString().ShouldBe("Pending");
        json.GetProperty("totalAmount").GetDecimal().ShouldBe(25m);
    }

    [Fact]
    public async Task Non_object_input_is_invalid_arguments()
    {
        var tool = new GetOrderStatusTool(new OrderFixture().ServiceFor(new OrderFixture().Customer("a@x.com")));
        var result = await Sut(tool).ExecuteAsync(new ToolCall("c1", "get_order_status", Json("\"ORD-20260101-ABCDEF\"")), Ct);

        result.IsError.ShouldBeTrue();
        result.Content.ShouldStartWith("Invalid arguments: ");
    }

    [Fact]
    public async Task Logs_outcome_without_argument_values()
    {
        var stub = new StubTool("t", _ => new { ok = true });
        await Sut(stub).ExecuteAsync(new ToolCall("c1", "t", Json("""{"orderNumber":"ORD-20260101-SECRET"}""")), Ct);

        _logger.Entries.ShouldContain(e => e.Message.Contains("t") && e.Message.Contains("ok"));
        _logger.Entries.ShouldNotContain(e => e.Message.Contains("SECRET"));
    }

    [Fact]
    public void Duplicate_tool_names_are_rejected() =>
        Should.Throw<InvalidOperationException>(() => Sut(new StubTool("t", _ => 1), new StubTool("t", _ => 2)));

    private sealed class StubTool(string name, Func<JsonElement, object> run) : IAssistantTool
    {
        public int Calls { get; private set; }
        public string Name => name;
        public string Description => "stub";
        public JsonElement InputSchema => JsonDocument.Parse("""{"type":"object"}""").RootElement;

        public Task<object> ExecuteAsync(JsonElement input, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(run(input));
        }
    }
}

public sealed class RecordingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception), exception));
}
