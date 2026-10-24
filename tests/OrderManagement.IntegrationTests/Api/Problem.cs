using System.Net;
using System.Text.Json;

using Shouldly;

namespace OrderManagement.IntegrationTests.Api;

/// <summary>Assertions for RFC 9457 ProblemDetails responses.</summary>
public static class Problem
{
    /// <summary>
    /// Asserts the status, the <c>application/problem+json</c> content type, a matching <c>status</c> member
    /// and a non-empty <c>traceId</c>. Returns the parsed body for further checks.
    /// </summary>
    public static async Task<JsonElement> AssertAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        response.StatusCode.ShouldBe(expected);
        response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/problem+json");

        string json = await response.Content.ReadAsStringAsync();
        JsonElement body = JsonDocument.Parse(json).RootElement.Clone();
        body.GetProperty("status").GetInt32().ShouldBe((int)expected);
        body.TryGetProperty("traceId", out JsonElement traceId).ShouldBeTrue($"no traceId in {json}");
        traceId.GetString().ShouldNotBeNullOrWhiteSpace();
        return body;
    }
}
