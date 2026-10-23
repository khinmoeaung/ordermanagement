using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using OrderManagement.Application.Common;
using OrderManagement.Application.Customers;
using OrderManagement.Application.Orders;
using OrderManagement.Application.Products;
using OrderManagement.Domain.Enums;

using Shouldly;

namespace OrderManagement.IntegrationTests.Api;

[Collection("Api")]
public sealed class OrderEndpointsTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static async Task<OrderResponse> PlaceAsync(HttpClient customer, ProductResponse product, int quantity)
    {
        HttpResponseMessage response = await customer.PostAsJsonAsync("/api/v1/orders",
            new PlaceOrderRequest([new PlaceOrderItem(product.Id, quantity)]), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        OrderResponse? order = await response.Content.ReadFromJsonAsync<OrderResponse>(Json, Ct);
        return order.ShouldNotBeNull();
    }

    private static async Task<OrderResponse> ActAsync(HttpClient client, Guid id, string action)
    {
        HttpResponseMessage response = await client.PostAsync($"/api/v1/orders/{id}/{action}", null, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        OrderResponse? order = await response.Content.ReadFromJsonAsync<OrderResponse>(Json, Ct);
        return order.ShouldNotBeNull();
    }

    private static async Task<Guid> CustomerIdAsync(RegisteredCustomer customer)
    {
        CustomerResponse? me = await customer.Client.GetFromJsonAsync<CustomerResponse>("/api/v1/customers/me", Ct);
        return me.ShouldNotBeNull().Id;
    }

    private static async Task<int> StockAsync(HttpClient admin, Guid productId)
    {
        ProductResponse? product = await admin.GetFromJsonAsync<ProductResponse>($"/api/v1/products/{productId}", Ct);
        return product.ShouldNotBeNull().StockQuantity;
    }

    [Fact]
    public async Task Full_lifecycle_to_completed()
    {
        HttpClient admin = await Api.LoginAdminAsync();
        RegisteredCustomer customer = await Api.RegisterCustomerAsync();
        ProductResponse product = await Api.CreateProductAsync(10, 5m);

        HttpResponseMessage created = await customer.Client.PostAsJsonAsync("/api/v1/orders",
            new PlaceOrderRequest([new PlaceOrderItem(product.Id, 3)]), Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        OrderResponse order = (await created.Content.ReadFromJsonAsync<OrderResponse>(Json, Ct))!;
        order.Status.ShouldBe(OrderStatus.Pending);
        order.TotalAmount.ShouldBe(15m);
        order.CustomerId.ShouldBe(await CustomerIdAsync(customer));
        created.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe($"/api/v1/orders/{order.Id}");
        (await StockAsync(admin, product.Id)).ShouldBe(7);

        (await ActAsync(admin, order.Id, "confirm")).Status.ShouldBe(OrderStatus.Confirmed);
        (await ActAsync(admin, order.Id, "ship")).Status.ShouldBe(OrderStatus.Shipped);
        (await ActAsync(admin, order.Id, "deliver")).Status.ShouldBe(OrderStatus.Delivered);
        (await ActAsync(customer.Client, order.Id, "complete")).Status.ShouldBe(OrderStatus.Completed);

        // Stock was deducted once, at placement, and not again by later transitions.
        (await StockAsync(admin, product.Id)).ShouldBe(7);
        OrderResponse? fetched = await customer.Client.GetFromJsonAsync<OrderResponse>($"/api/v1/orders/{order.Id}", Json, Ct);
        fetched.ShouldNotBeNull().Status.ShouldBe(OrderStatus.Completed);

        PagedResult<OrderResponse>? mine = await customer.Client.GetFromJsonAsync<PagedResult<OrderResponse>>(
            "/api/v1/orders/my", Json, Ct);
        mine.ShouldNotBeNull().Items.ShouldHaveSingleItem().Id.ShouldBe(order.Id);
    }

    [Fact]
    public async Task Cancel_restores_stock()
    {
        HttpClient admin = await Api.LoginAdminAsync();
        RegisteredCustomer customer = await Api.RegisterCustomerAsync();
        ProductResponse product = await Api.CreateProductAsync(10, 5m);
        OrderResponse order = await PlaceAsync(customer.Client, product, 4);
        (await StockAsync(admin, product.Id)).ShouldBe(6);

        (await ActAsync(customer.Client, order.Id, "cancel")).Status.ShouldBe(OrderStatus.Cancelled);

        (await StockAsync(admin, product.Id)).ShouldBe(10);
    }

    [Fact]
    public async Task Illegal_transition_returns_409()
    {
        HttpClient admin = await Api.LoginAdminAsync();
        RegisteredCustomer customer = await Api.RegisterCustomerAsync();
        ProductResponse product = await Api.CreateProductAsync(5, 5m);
        OrderResponse order = await PlaceAsync(customer.Client, product, 1);

        HttpResponseMessage response = await admin.PostAsync($"/api/v1/orders/{order.Id}/ship", null, Ct);

        await Problem.AssertAsync(response, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Insufficient_stock_returns_409()
    {
        RegisteredCustomer customer = await Api.RegisterCustomerAsync();
        ProductResponse product = await Api.CreateProductAsync(2, 5m);

        HttpResponseMessage response = await customer.Client.PostAsJsonAsync("/api/v1/orders",
            new PlaceOrderRequest([new PlaceOrderItem(product.Id, 3)]), Ct);

        await Problem.AssertAsync(response, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Other_customers_order_returns_404()
    {
        RegisteredCustomer owner = await Api.RegisterCustomerAsync();
        RegisteredCustomer other = await Api.RegisterCustomerAsync();
        ProductResponse product = await Api.CreateProductAsync(5, 5m);
        OrderResponse order = await PlaceAsync(owner.Client, product, 1);

        await Problem.AssertAsync(await other.Client.GetAsync($"/api/v1/orders/{order.Id}", Ct), HttpStatusCode.NotFound);
        await Problem.AssertAsync(await other.Client.PostAsync($"/api/v1/orders/{order.Id}/cancel", null, Ct),
            HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Customer_cannot_confirm_403()
    {
        RegisteredCustomer customer = await Api.RegisterCustomerAsync();
        ProductResponse product = await Api.CreateProductAsync(5, 5m);
        OrderResponse order = await PlaceAsync(customer.Client, product, 1);

        HttpResponseMessage response = await customer.Client.PostAsync($"/api/v1/orders/{order.Id}/confirm", null, Ct);

        await Problem.AssertAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_cannot_place_order_403()
    {
        HttpClient admin = await Api.LoginAdminAsync();
        ProductResponse product = await Api.CreateProductAsync(5, 5m);

        HttpResponseMessage response = await admin.PostAsJsonAsync("/api/v1/orders",
            new PlaceOrderRequest([new PlaceOrderItem(product.Id, 1)]), Ct);

        await Problem.AssertAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_filters_orders_by_status()
    {
        HttpClient admin = await Api.LoginAdminAsync();
        RegisteredCustomer customer = await Api.RegisterCustomerAsync();
        ProductResponse product = await Api.CreateProductAsync(10, 5m);
        OrderResponse pending = await PlaceAsync(customer.Client, product, 1);
        OrderResponse confirmed = await PlaceAsync(customer.Client, product, 1);
        await ActAsync(admin, confirmed.Id, "confirm");

        PagedResult<OrderResponse>? onlyConfirmed = await admin.GetFromJsonAsync<PagedResult<OrderResponse>>(
            "/api/v1/orders?status=Confirmed", Json, Ct);
        onlyConfirmed.ShouldNotBeNull().Items.ShouldHaveSingleItem().Id.ShouldBe(confirmed.Id);

        PagedResult<OrderResponse>? onlyPending = await admin.GetFromJsonAsync<PagedResult<OrderResponse>>(
            $"/api/v1/orders?status=Pending&customerId={await CustomerIdAsync(customer)}", Json, Ct);
        onlyPending.ShouldNotBeNull().Items.ShouldHaveSingleItem().Id.ShouldBe(pending.Id);

        PagedResult<OrderResponse>? all = await admin.GetFromJsonAsync<PagedResult<OrderResponse>>("/api/v1/orders", Json, Ct);
        all.ShouldNotBeNull().TotalCount.ShouldBe(2);

        // Customers cannot use the admin list.
        await Problem.AssertAsync(await customer.Client.GetAsync("/api/v1/orders", Ct), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Non_guid_id_returns_404()
    {
        RegisteredCustomer customer = await Api.RegisterCustomerAsync();

        HttpResponseMessage response = await customer.Client.GetAsync("/api/v1/orders/abc", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        string body = await response.Content.ReadAsStringAsync(Ct);
        if (body.Length > 0)
        {
            response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/problem+json");
        }
    }

    [Fact]
    public async Task Unknown_status_filter_returns_400()
    {
        HttpClient admin = await Api.LoginAdminAsync();

        HttpResponseMessage response = await admin.GetAsync("/api/v1/orders?status=Foo", Ct);

        await Problem.AssertAsync(response, HttpStatusCode.BadRequest);
    }
}
