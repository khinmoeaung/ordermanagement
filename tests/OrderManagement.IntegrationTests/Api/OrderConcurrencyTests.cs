using System.Net;
using System.Net.Http.Json;

using OrderManagement.Application.Orders;
using OrderManagement.Application.Products;

using Shouldly;

namespace OrderManagement.IntegrationTests.Api;

[Collection("Api")]
public sealed class OrderConcurrencyTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_parallel_orders_for_last_unit_exactly_one_succeeds()
    {
        HttpClient admin = await Api.LoginAdminAsync();
        ProductResponse product = await Api.CreateProductAsync(1, 10m);
        RegisteredCustomer first = await Api.RegisterCustomerAsync();
        RegisteredCustomer second = await Api.RegisterCustomerAsync();
        PlaceOrderRequest request = new([new PlaceOrderItem(product.Id, 1)]);

        HttpResponseMessage[] responses = await Task.WhenAll(
            first.Client.PostAsJsonAsync("/api/v1/orders", request, Ct),
            second.Client.PostAsJsonAsync("/api/v1/orders", request, Ct));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        HttpResponseMessage loser = responses.Single(r => r.StatusCode != HttpStatusCode.Created);
        await Problem.AssertAsync(loser, HttpStatusCode.Conflict);

        ProductResponse? after = await admin.GetFromJsonAsync<ProductResponse>($"/api/v1/products/{product.Id}", Ct);
        after.ShouldNotBeNull().StockQuantity.ShouldBe(0);
    }
}
