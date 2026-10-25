using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace OrderManagement.IntegrationTests.Api;

public sealed class CorsTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    private const string WebUi = "http://localhost:4200";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> WithOrigins(params string[] origins) =>
        Factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(origins.Select((o, i) => KeyValuePair.Create<string, string?>($"Cors:AllowedOrigins:{i}", o)))));

    private static HttpRequestMessage Request(HttpMethod method, string path, string? origin)
    {
        var request = new HttpRequestMessage(method, path);
        if (origin is not null) request.Headers.Add("Origin", origin);
        return request;
    }

    private static string? AllowOrigin(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) ? values.Single() : null;

    [Fact]
    public async Task Preflight_from_the_configured_origin_is_allowed()
    {
        using var app = WithOrigins(WebUi);
        using var client = app.CreateClient();
        var request = Request(HttpMethod.Options, "/api/v1/orders", WebUi);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");

        var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        AllowOrigin(response).ShouldBe(WebUi);
        string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers")).ShouldContain("authorization", Case.Insensitive);
        string.Join(",", response.Headers.GetValues("Access-Control-Allow-Methods")).ShouldContain("POST");
        response.Headers.Contains("Access-Control-Allow-Credentials").ShouldBeFalse();   // bearer tokens, no cookies
    }

    [Fact]
    public async Task Actual_request_from_the_configured_origin_gets_the_allow_origin_header()
    {
        using var app = WithOrigins(WebUi);
        using var client = app.CreateClient();

        var response = await client.SendAsync(Request(HttpMethod.Get, "/api/v1/products", WebUi), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        AllowOrigin(response).ShouldBe(WebUi);
    }

    [Fact]
    public async Task Error_responses_keep_the_cors_header_so_the_browser_can_read_them()
    {
        using var app = WithOrigins(WebUi);
        using var client = app.CreateClient();

        var unauthorized = await client.SendAsync(Request(HttpMethod.Get, "/api/v1/orders/my", WebUi), Ct);
        var notFound = await client.SendAsync(Request(HttpMethod.Get, "/api/v1/nope", WebUi), Ct);

        unauthorized.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        AllowOrigin(unauthorized).ShouldBe(WebUi);
        notFound.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        AllowOrigin(notFound).ShouldBe(WebUi);
    }

    [Fact]
    public async Task Other_origins_get_no_allow_origin_header()
    {
        using var app = WithOrigins(WebUi);
        using var client = app.CreateClient();

        var response = await client.SendAsync(Request(HttpMethod.Get, "/api/v1/products", "http://evil.example"), Ct);

        AllowOrigin(response).ShouldBeNull();
    }

    [Fact]
    public async Task With_no_origins_configured_nothing_is_allowed()
    {
        var response = await Client.SendAsync(Request(HttpMethod.Get, "/api/v1/products", WebUi), Ct);

        AllowOrigin(response).ShouldBeNull();
    }

    [Theory]
    [InlineData("localhost:4200")]
    [InlineData("http://localhost:4200/app")]
    [InlineData("ftp://localhost")]
    [InlineData("*")]
    public void An_invalid_origin_stops_the_app_from_starting(string origin)
    {
        using var app = WithOrigins(origin);

        Should.Throw<Exception>(() => app.CreateClient()).ToString().ShouldContain("Cors:AllowedOrigins");
    }
}
