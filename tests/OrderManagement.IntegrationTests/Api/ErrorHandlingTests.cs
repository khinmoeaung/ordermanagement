using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using OrderManagement.Application.Auth;

using Shouldly;

namespace OrderManagement.IntegrationTests.Api;

[Collection("Api")]
public sealed class ErrorHandlingTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Validation_error_returns_400_with_field_errors()
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest("weak@test.local", "short", "Jane Doe", "+1 555 0100", "1 Main Street"),
            TestContext.Current.CancellationToken);

        JsonElement body = await Problem.AssertAsync(response, HttpStatusCode.BadRequest);
        JsonElement errors = body.GetProperty("errors");
        errors.TryGetProperty("Password", out JsonElement password).ShouldBeTrue(errors.ToString());
        password.GetArrayLength().ShouldBeGreaterThan(0);
        errors.TryGetProperty("Email", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Malformed_json_returns_400_problem()
    {
        using StringContent content = new("{\"email\":", Encoding.UTF8, "application/json");

        HttpResponseMessage response = await Client.PostAsync("/api/v1/auth/login", content,
            TestContext.Current.CancellationToken);

        await Problem.AssertAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Problem_responses_include_traceId()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        // 401 from the authorization middleware (no token on an [Authorize] endpoint).
        HttpResponseMessage challenge = await Client.PostAsJsonAsync("/api/v1/auth/logout", new LogoutRequest("x"), ct);
        await Problem.AssertAsync(challenge, HttpStatusCode.Unauthorized);

        // 401 from the exception handler (AuthenticationFailedException).
        HttpResponseMessage badRefresh = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest("unknown"), ct);
        await Problem.AssertAsync(badRefresh, HttpStatusCode.Unauthorized);

        // 400 from the validation filter.
        HttpResponseMessage invalid = await Client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("", ""), ct);
        await Problem.AssertAsync(invalid, HttpStatusCode.BadRequest);

        // 404 for an unknown route (status code pages).
        HttpResponseMessage missing = await Client.GetAsync("/api/v1/does-not-exist", ct);
        await Problem.AssertAsync(missing, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Health_returns_200()
    {
        HttpResponseMessage response = await Client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
