using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Api.Infrastructure;
using Microsoft.Extensions.Logging;
using OrderManagement.Application.Assistant;
using OrderManagement.Application.Orders;
using Shouldly;

namespace OrderManagement.IntegrationTests.Api;

public sealed class AssistantEndpointsTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    private const string Url = "/api/v1/assistant/chat";
    private static readonly JsonSerializerOptions EnumJson = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static ModelTurn Answer(string text) => new(ModelStopReason.EndTurn, text, []);

    private static ModelTurn CallGetOrderStatus(string orderNumber) => new(ModelStopReason.ToolUse, "",
        [new ToolCall("tu_1", "get_order_status", JsonSerializer.SerializeToElement(new { orderNumber }))]);

    private async Task<OrderResponse> PlaceOrderAsync(RegisteredCustomer customer)
    {
        var product = await Api.CreateProductAsync(stock: 10, price: 12.50m);
        var response = await customer.Client.PostAsJsonAsync("/api/v1/orders",
            new PlaceOrderRequest([new PlaceOrderItem(product.Id, 2)]), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<OrderResponse>(EnumJson, TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task Anonymous_gets_401()
    {
        var response = await Client.PostAsJsonAsync(Url, new ChatRequest("hi"), TestContext.Current.CancellationToken);
        await Problem.AssertAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_gets_403()
    {
        var admin = await Api.LoginAdminAsync();
        var response = await admin.PostAsJsonAsync(Url, new ChatRequest("hi"), TestContext.Current.CancellationToken);
        await Problem.AssertAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Customer_gets_reply()
    {
        var customer = await Api.RegisterCustomerAsync();
        Factory.Assistant.Script(_ => Answer("Hello"));

        var response = await customer.Client.PostAsJsonAsync(Url, new ChatRequest("hi"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ChatResponse>(TestContext.Current.CancellationToken))!.Reply.ShouldBe("Hello");
    }

    [Fact]
    public async Task Customer_cannot_see_another_customers_order()
    {
        var a = await Api.RegisterCustomerAsync();
        var b = await Api.RegisterCustomerAsync();
        var aOrder = await PlaceOrderAsync(a);

        // The model is tricked into asking for A's order while B is signed in.
        Factory.Assistant.Script(
            _ => CallGetOrderStatus(aOrder.OrderNumber),
            results => Answer("I couldn't find that order on your account."));

        var response = await b.Client.PostAsJsonAsync(Url, new ChatRequest($"Status of {aOrder.OrderNumber}?"), TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = Factory.Assistant.ReceivedResults.ShouldHaveSingleItem().ShouldHaveSingleItem();
        result.ShouldBe(new ToolResult("tu_1", "Order not found.", true));
        foreach (string leaked in new[] { aOrder.Id.ToString(), aOrder.CustomerId.ToString(), "Pending", "Test product", "25" })
            result.Content.ShouldNotContain(leaked);
        body.ShouldNotContain(aOrder.Id.ToString());
    }

    [Fact]
    public async Task Own_order_returns_restricted_result()
    {
        var a = await Api.RegisterCustomerAsync();
        var order = await PlaceOrderAsync(a);
        Factory.Assistant.Script(
            _ => CallGetOrderStatus(order.OrderNumber.ToLowerInvariant()),
            _ => Answer("Your order is pending."));

        var response = await a.Client.PostAsJsonAsync(Url, new ChatRequest("Where is my order?"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = Factory.Assistant.ReceivedResults.ShouldHaveSingleItem().ShouldHaveSingleItem();
        result.IsError.ShouldBeFalse();
        var json = JsonDocument.Parse(result.Content).RootElement;
        json.GetProperty("orderNumber").GetString().ShouldBe(order.OrderNumber);
        json.GetProperty("status").GetString().ShouldBe("Pending");
        result.Content.ShouldNotContain(order.Id.ToString());
        result.Content.ShouldNotContain(order.CustomerId.ToString());
    }

    [Fact]
    public async Task Blank_message_returns_400()
    {
        var customer = await Api.RegisterCustomerAsync();
        var response = await customer.Client.PostAsJsonAsync(Url, new ChatRequest("   "), TestContext.Current.CancellationToken);
        await Problem.AssertAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Not_configured_returns_503()
    {
        using var realModelFactory = Factory.WithRealAssistantModel();
        using var anonymous = realModelFactory.CreateClient();
        var register = await anonymous.PostAsJsonAsync("/api/v1/auth/register", new Application.Auth.RegisterRequest(
            $"c-{Guid.NewGuid():N}@test.local", TestClient.CustomerPassword, "Test Customer", "+1 555 0100", "1 Test Street"),
            TestContext.Current.CancellationToken);
        var auth = (await register.Content.ReadFromJsonAsync<Application.Auth.AuthResponse>(TestContext.Current.CancellationToken))!;
        anonymous.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);

        var response = await anonymous.PostAsJsonAsync(Url, new ChatRequest("hi"), TestContext.Current.CancellationToken);

        await Problem.AssertAsync(response, HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("Assistant is not configured.");
    }

    private static IEnumerable<CapturedLog> HandlerWarnings(CapturingLoggerProvider logs) =>
        logs.Entries.Where(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Category.EndsWith("GlobalExceptionHandler"));

    [Fact]
    public async Task Unavailable_provider_returns_503_and_logs_type_only()
    {
        var customer = await Api.RegisterCustomerAsync();
        var logs = new CapturingLoggerProvider();
        using var logging = Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton(logs.For<GlobalExceptionHandler>())));
        using var client = logging.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", customer.Auth.AccessToken);
        Factory.Assistant.Script(_ => throw new AssistantUnavailableException(new HttpRequestException("inner-secret-detail")));

        var response = await client.PostAsJsonAsync(Url, new ChatRequest("hi"), TestContext.Current.CancellationToken);

        await Problem.AssertAsync(response, HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("Assistant is temporarily unavailable.");

        var warning = HandlerWarnings(logs).ShouldHaveSingleItem();
        warning.Message.ShouldContain("AssistantUnavailableException");
        warning.Message.ShouldContain("HttpRequestException");
        logs.Entries.ShouldAllBe(e => !e.Message.Contains("inner-secret-detail") && (e.ExceptionText == null || !e.ExceptionText.Contains("inner-secret-detail")));
    }

    [Fact]
    public async Task Not_configured_is_logged_at_warning()
    {
        var customer = await Api.RegisterCustomerAsync();
        var logs = new CapturingLoggerProvider();
        using var realModelFactory = Factory.WithRealAssistantModel(logs.For<GlobalExceptionHandler>());
        using var client = realModelFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", customer.Auth.AccessToken);

        var response = await client.PostAsJsonAsync(Url, new ChatRequest("hi"), TestContext.Current.CancellationToken);

        await Problem.AssertAsync(response, HttpStatusCode.ServiceUnavailable);
        HandlerWarnings(logs).ShouldHaveSingleItem().Message.ShouldContain("AssistantNotConfiguredException");
    }
}
