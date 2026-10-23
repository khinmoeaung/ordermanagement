using System.Net;
using System.Net.Http.Json;
using System.Text;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using OrderManagement.Application.Auth;

using Shouldly;

namespace OrderManagement.IntegrationTests.Api;

[Collection("Api")]
public sealed class AuthEndpointsTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    private static RegisterRequest NewRegistration(string email, string password = "Secret123") =>
        new(email, password, "Jane Doe", "+1 555 0100", "1 Main Street");

    [Fact]
    public async Task Register_returns_201_with_tokens()
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/auth/register",
            NewRegistration("jane@test.local"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        AuthResponse? body = await response.Content.ReadFromJsonAsync<AuthResponse>(TestContext.Current.CancellationToken);
        body.ShouldNotBeNull();
        body.AccessToken.ShouldNotBeNullOrWhiteSpace();
        body.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        body.AccessTokenExpiresAt.ShouldBeGreaterThan(DateTime.UtcNow);
        body.RefreshTokenExpiresAt.ShouldBeGreaterThan(body.AccessTokenExpiresAt);
    }

    [Fact]
    public async Task Register_duplicate_email_returns_409_problem()
    {
        await Api.RegisterCustomerAsync("dup@test.local");

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/auth/register",
            NewRegistration("DUP@test.local"), TestContext.Current.CancellationToken);

        await Problem.AssertAsync(response, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Login_bad_credentials_returns_401_problem()
    {
        HttpResponseMessage wrongPassword = await Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(ApiFactory.AdminEmail, "Wrong12345"), TestContext.Current.CancellationToken);
        HttpResponseMessage unknownEmail = await Client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("nobody@test.local", "Wrong12345"), TestContext.Current.CancellationToken);

        var a = await Problem.AssertAsync(wrongPassword, HttpStatusCode.Unauthorized);
        var b = await Problem.AssertAsync(unknownEmail, HttpStatusCode.Unauthorized);
        a.GetProperty("detail").GetString().ShouldBe(b.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Refresh_rotates_and_old_token_reuse_returns_401_and_revokes_new()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        RegisteredCustomer customer = await Api.RegisterCustomerAsync();
        string first = customer.Auth.RefreshToken;

        HttpResponseMessage rotated = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(first), ct);
        rotated.StatusCode.ShouldBe(HttpStatusCode.OK);
        AuthResponse second = (await rotated.Content.ReadFromJsonAsync<AuthResponse>(ct))!;
        second.RefreshToken.ShouldNotBe(first);

        HttpResponseMessage reuse = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(first), ct);
        await Problem.AssertAsync(reuse, HttpStatusCode.Unauthorized);

        HttpResponseMessage afterReuse = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(second.RefreshToken), ct);
        await Problem.AssertAsync(afterReuse, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_returns_204_and_token_no_longer_refreshes()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        RegisteredCustomer customer = await Api.RegisterCustomerAsync();

        HttpResponseMessage logout = await customer.Client.PostAsJsonAsync("/api/v1/auth/logout",
            new LogoutRequest(customer.Auth.RefreshToken), ct);
        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        HttpResponseMessage refresh = await Client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest(customer.Auth.RefreshToken), ct);
        await Problem.AssertAsync(refresh, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Expired_access_token_returns_401()
    {
        RegisteredCustomer customer = await Api.RegisterCustomerAsync();
        DateTime now = DateTime.UtcNow;
        string expired = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = ApiFactory.JwtIssuer,
            Audience = ApiFactory.JwtAudience,
            IssuedAt = now.AddMinutes(-20),
            NotBefore = now.AddMinutes(-20),
            Expires = now.AddMinutes(-5),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = customer.UserId.ToString(),
                ["email"] = customer.Email,
                ["role"] = "Customer",
                ["jti"] = Guid.NewGuid().ToString()
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.JwtSigningKey)),
                SecurityAlgorithms.HmacSha256)
        });

        using HttpClient client = Api.CreateAuthenticated(expired);
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/auth/logout",
            new LogoutRequest(customer.Auth.RefreshToken), TestContext.Current.CancellationToken);

        await Problem.AssertAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Tampered_access_token_returns_401()
    {
        RegisteredCustomer customer = await Api.RegisterCustomerAsync();
        string token = customer.Auth.AccessToken;
        // The HS256 signature is 32 bytes, so the last base64url char carries 4 real bits and 2 padding bits.
        // Shift by 16 positions so a real bit changes, not just the padding.
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        char replacement = Alphabet[(Alphabet.IndexOf(token[^1]) + 16) % 64];
        string tampered = token[..^1] + replacement;
        tampered.ShouldNotBe(token);

        using HttpClient client = Api.CreateAuthenticated(tampered);
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/auth/logout",
            new LogoutRequest(customer.Auth.RefreshToken), TestContext.Current.CancellationToken);

        await Problem.AssertAsync(response, HttpStatusCode.Unauthorized);
    }
}
