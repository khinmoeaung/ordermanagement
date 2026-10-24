using System.Net;
using System.Net.Http.Json;

using OrderManagement.Application.Common;
using OrderManagement.Application.Customers;

using Shouldly;

namespace OrderManagement.IntegrationTests.Api;

[Collection("Api")]
public sealed class CustomerEndpointsTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Customer_gets_and_updates_own_profile()
    {
        RegisteredCustomer customer = await Api.RegisterCustomerAsync();

        CustomerResponse? me = await customer.Client.GetFromJsonAsync<CustomerResponse>("/api/v1/customers/me", Ct);
        me.ShouldNotBeNull();
        me.Email.ShouldBe(customer.Email);

        HttpResponseMessage updated = await customer.Client.PutAsJsonAsync("/api/v1/customers/me",
            new UpdateCustomerRequest("New Name", "+1 555 0199", "9 New Street"), Ct);
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        CustomerResponse? body = await updated.Content.ReadFromJsonAsync<CustomerResponse>(Ct);
        body.ShouldNotBeNull();
        body.FullName.ShouldBe("New Name");
        body.Phone.ShouldBe("+1 555 0199");
        body.ShippingAddress.ShouldBe("9 New Street");
        body.Email.ShouldBe(customer.Email);
        body.Id.ShouldBe(me.Id);

        CustomerResponse? again = await customer.Client.GetFromJsonAsync<CustomerResponse>("/api/v1/customers/me", Ct);
        again.ShouldBe(body);
    }

    [Fact]
    public async Task Customer_cannot_list_customers_403()
    {
        RegisteredCustomer customer = await Api.RegisterCustomerAsync();

        HttpResponseMessage response = await customer.Client.GetAsync("/api/v1/customers", Ct);

        await Problem.AssertAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_lists_customers_with_search()
    {
        HttpClient admin = await Api.LoginAdminAsync();
        string email = $"findme-{Guid.NewGuid():N}@test.local";
        await Api.RegisterCustomerAsync(email);
        await Api.RegisterCustomerAsync();

        PagedResult<CustomerResponse>? all = await admin.GetFromJsonAsync<PagedResult<CustomerResponse>>(
            "/api/v1/customers", Ct);
        all.ShouldNotBeNull();
        all.TotalCount.ShouldBe(2);

        PagedResult<CustomerResponse>? found = await admin.GetFromJsonAsync<PagedResult<CustomerResponse>>(
            $"/api/v1/customers?search={email}", Ct);
        found.ShouldNotBeNull();
        CustomerResponse hit = found.Items.ShouldHaveSingleItem();
        hit.Email.ShouldBe(email);

        HttpResponseMessage byId = await admin.GetAsync($"/api/v1/customers/{hit.Id}", Ct);
        byId.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await byId.Content.ReadFromJsonAsync<CustomerResponse>(Ct)).ShouldBe(hit);

        await Problem.AssertAsync(await admin.GetAsync($"/api/v1/customers/{Guid.NewGuid()}", Ct), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_cannot_use_customers_me_403()
    {
        HttpClient admin = await Api.LoginAdminAsync();

        HttpResponseMessage response = await admin.GetAsync("/api/v1/customers/me", Ct);

        await Problem.AssertAsync(response, HttpStatusCode.Forbidden);
    }
}
