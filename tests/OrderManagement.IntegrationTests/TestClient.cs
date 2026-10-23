using System.Net.Http.Headers;
using System.Net.Http.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

using OrderManagement.Application.Auth;
using OrderManagement.Application.Products;
using OrderManagement.Domain.Entities;
using OrderManagement.Infrastructure.Persistence;

using Shouldly;

namespace OrderManagement.IntegrationTests;

/// <summary>A registered customer: an authenticated client plus the tokens and IDs behind it.</summary>
public sealed record RegisteredCustomer(HttpClient Client, AuthResponse Auth, string Email, string Password, Guid UserId);

/// <summary>Test helpers that drive the API (or, where no endpoint exists yet, the database) to set up state.</summary>
public sealed class TestClient(ApiFactory factory)
{
    public const string CustomerPassword = "Customer123";

    /// <summary>An unauthenticated client.</summary>
    public HttpClient CreateAnonymous() => factory.CreateClient();

    /// <summary>A client that sends <paramref name="accessToken"/> as a Bearer token.</summary>
    public HttpClient CreateAuthenticated(string accessToken)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    /// <summary>Registers a customer through <c>POST /api/v1/auth/register</c>. A unique email is generated if none is given.</summary>
    public async Task<RegisteredCustomer> RegisterCustomerAsync(string? email = null)
    {
        email ??= $"customer-{Guid.NewGuid():N}@test.local";
        using HttpClient anonymous = CreateAnonymous();
        HttpResponseMessage response = await anonymous.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(
            email, CustomerPassword, "Test Customer", "+1 555 0100", "1 Test Street, Testville"));
        response.EnsureSuccessStatusCode();
        AuthResponse auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        Guid userId = Guid.Parse(new JsonWebToken(auth.AccessToken).Subject);
        return new RegisteredCustomer(CreateAuthenticated(auth.AccessToken), auth, email, CustomerPassword, userId);
    }

    /// <summary>Logs in as the seeded admin and returns a client with the Bearer header set.</summary>
    public async Task<HttpClient> LoginAdminAsync()
    {
        using HttpClient anonymous = CreateAnonymous();
        HttpResponseMessage response = await anonymous.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(ApiFactory.AdminEmail, ApiFactory.AdminPassword));
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        AuthResponse auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        return CreateAuthenticated(auth.AccessToken);
    }

    /// <summary>Inserts an active product with a unique SKU directly into the database.</summary>
    public async Task<ProductResponse> CreateProductAsync(int stock, decimal price)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        string sku = $"T-{Guid.NewGuid():N}"[..16].ToUpperInvariant();
        Product product = Product.Create(sku, $"Test product {sku}", null, price, stock, DateTime.UtcNow);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.ToResponse();
    }
}
