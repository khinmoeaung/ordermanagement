using System.Net;
using System.Net.Http.Json;

using OrderManagement.Application.Common;
using OrderManagement.Application.Products;

using Shouldly;

namespace OrderManagement.IntegrationTests.Api;

[Collection("Api")]
public sealed class ProductEndpointsTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Anonymous_can_list_active_products_only()
    {
        HttpClient admin = await Api.LoginAdminAsync();
        ProductResponse active = await Api.CreateProductAsync(5, 10m);
        ProductResponse inactive = await Api.CreateProductAsync(5, 10m);
        (await admin.DeleteAsync($"/api/v1/products/{inactive.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        HttpResponseMessage response = await Client.GetAsync("/api/v1/products", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        PagedResult<ProductResponse>? page = await response.Content.ReadFromJsonAsync<PagedResult<ProductResponse>>(Ct);
        page.ShouldNotBeNull();
        page.Items.ShouldContain(p => p.Id == active.Id);
        page.Items.ShouldNotContain(p => p.Id == inactive.Id);
        page.Items.ShouldAllBe(p => p.IsActive);
        page.Page.ShouldBe(1);
        page.PageSize.ShouldBe(20);
        page.TotalCount.ShouldBe(page.Items.Count);

        // includeInactive is ignored for anonymous callers.
        PagedResult<ProductResponse>? ignored = await Client.GetFromJsonAsync<PagedResult<ProductResponse>>(
            "/api/v1/products?includeInactive=true", Ct);
        ignored!.Items.ShouldNotContain(p => p.Id == inactive.Id);
    }

    [Fact]
    public async Task Admin_crud_product_flow()
    {
        HttpClient admin = await Api.LoginAdminAsync();
        string sku = $"CRUD-{Guid.NewGuid():N}"[..14].ToUpperInvariant();

        // create 201 with Location
        HttpResponseMessage created = await admin.PostAsJsonAsync("/api/v1/products",
            new CreateProductRequest(sku, "Widget", "A widget", 12.50m, 7), Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        ProductResponse? product = await created.Content.ReadFromJsonAsync<ProductResponse>(Ct);
        product.ShouldNotBeNull();
        product.Sku.ShouldBe(sku);
        product.IsActive.ShouldBeTrue();
        created.Headers.Location.ShouldNotBeNull();
        created.Headers.Location.AbsolutePath.ShouldBe($"/api/v1/products/{product.Id}");

        HttpResponseMessage fetched = await Client.GetAsync(created.Headers.Location, Ct);
        fetched.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await fetched.Content.ReadFromJsonAsync<ProductResponse>(Ct)).ShouldBe(product);

        // update 200
        HttpResponseMessage updated = await admin.PutAsJsonAsync($"/api/v1/products/{product.Id}",
            new UpdateProductRequest("Widget v2", null, 15m, 3), Ct);
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        ProductResponse? updatedBody = await updated.Content.ReadFromJsonAsync<ProductResponse>(Ct);
        updatedBody.ShouldNotBeNull();
        updatedBody.Name.ShouldBe("Widget v2");
        updatedBody.Price.ShouldBe(15m);
        updatedBody.StockQuantity.ShouldBe(3);
        updatedBody.Sku.ShouldBe(sku);

        // duplicate SKU 409
        HttpResponseMessage duplicate = await admin.PostAsJsonAsync("/api/v1/products",
            new CreateProductRequest(sku, "Other", null, 1m, 1), Ct);
        await Problem.AssertAsync(duplicate, HttpStatusCode.Conflict);

        // delete 204
        (await admin.DeleteAsync($"/api/v1/products/{product.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // anonymous GET 404
        await Problem.AssertAsync(await Client.GetAsync($"/api/v1/products/{product.Id}", Ct), HttpStatusCode.NotFound);

        // admin list with includeInactive shows it, inactive
        PagedResult<ProductResponse>? list = await admin.GetFromJsonAsync<PagedResult<ProductResponse>>(
            $"/api/v1/products?includeInactive=true&search={sku}", Ct);
        list.ShouldNotBeNull();
        ProductResponse inList = list.Items.ShouldHaveSingleItem();
        inList.Id.ShouldBe(product.Id);
        inList.IsActive.ShouldBeFalse();

        // admin GET {id} returns the inactive product directly
        HttpResponseMessage adminGet = await admin.GetAsync($"/api/v1/products/{product.Id}", Ct);
        adminGet.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await adminGet.Content.ReadFromJsonAsync<ProductResponse>(Ct))!.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Customer_cannot_create_product_403()
    {
        RegisteredCustomer customer = await Api.RegisterCustomerAsync();

        HttpResponseMessage response = await customer.Client.PostAsJsonAsync("/api/v1/products",
            new CreateProductRequest("NOPE-1", "Nope", null, 1m, 1), Ct);

        await Problem.AssertAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Anonymous_cannot_create_product_401()
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/products",
            new CreateProductRequest("NOPE-2", "Nope", null, 1m, 1), Ct);

        await Problem.AssertAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PageSize_over_100_returns_400()
    {
        HttpResponseMessage response = await Client.GetAsync("/api/v1/products?pageSize=101", Ct);

        await Problem.AssertAsync(response, HttpStatusCode.BadRequest);
    }
}
