using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderManagement.Api.Infrastructure;
using OrderManagement.Application.Policies;
using OrderManagement.Infrastructure.Policies;
using Shouldly;

namespace OrderManagement.IntegrationTests.Api;

public sealed class PolicyEndpointsTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    private const string AskUrl = "/api/v1/policies/ask";
    private const string ReindexUrl = "/api/v1/policies/reindex";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpClient ClientFor(WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private async Task<string> AdminTokenAsync()
    {
        using var admin = await Api.LoginAdminAsync();
        return admin.DefaultRequestHeaders.Authorization!.Parameter!;
    }

    private async Task<(WebApplicationFactory<Program> Fakes, HttpClient Client)> CustomerWithFakesAsync(ILogger<GlobalExceptionHandler>? logger = null)
    {
        var customer = await Api.RegisterCustomerAsync();
        var fakes = Factory.WithFakePolicies(logger);
        return (fakes, ClientFor(fakes, customer.Auth.AccessToken));
    }

    [Fact]
    public async Task Ask_without_a_token_returns_401()
    {
        var response = await Client.PostAsJsonAsync(AskUrl, new AskPolicyRequest("Can I cancel?"), Ct);

        await Problem.AssertAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Ask_with_a_customer_token_returns_the_answer_and_citations()
    {
        Factory.Policies.OnAsk = _ => new PolicyAnswer("Yes, while Pending or Confirmed [1].",
            [new PolicyCitation("cancellation.md", "Cancellation Policy", "Cancellation Policy > When an order can be cancelled", "An order can be cancelled while...")]);
        var (fakes, client) = await CustomerWithFakesAsync();
        using (fakes) using (client)
        {
            var response = await client.PostAsJsonAsync(AskUrl, new AskPolicyRequest("Can I cancel my order?"), Ct);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
            body.GetProperty("answer").GetString().ShouldBe("Yes, while Pending or Confirmed [1].");
            var citation = body.GetProperty("citations").EnumerateArray().Single();
            citation.GetProperty("source").GetString().ShouldBe("cancellation.md");
            citation.GetProperty("title").GetString().ShouldBe("Cancellation Policy");
            citation.GetProperty("section").GetString().ShouldBe("Cancellation Policy > When an order can be cancelled");
            citation.GetProperty("excerpt").GetString().ShouldBe("An order can be cancelled while...");
            Factory.Policies.Questions.ShouldBe(["Can I cancel my order?"]);
        }
    }

    [Fact]
    public async Task Ask_with_an_admin_token_works_too()
    {
        using var fakes = Factory.WithFakePolicies();
        using var client = ClientFor(fakes, await AdminTokenAsync());

        var response = await client.PostAsJsonAsync(AskUrl, new AskPolicyRequest("How long is delivery?"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Ask_with_a_blank_question_returns_400(string question)
    {
        var (fakes, client) = await CustomerWithFakesAsync();
        using (fakes) using (client)
        {
            var response = await client.PostAsJsonAsync(AskUrl, new AskPolicyRequest(question), Ct);

            await Problem.AssertAsync(response, HttpStatusCode.BadRequest);
            Factory.Policies.Questions.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Ask_with_an_over_long_question_returns_400()
    {
        var (fakes, client) = await CustomerWithFakesAsync();
        using (fakes) using (client)
        {
            var response = await client.PostAsJsonAsync(AskUrl, new AskPolicyRequest(new string('a', 1001)), Ct);

            await Problem.AssertAsync(response, HttpStatusCode.BadRequest);
            Factory.Policies.Questions.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Ask_not_configured_returns_503_with_the_exact_detail() =>
        await AssertAskFailsAsync(new PolicySearchNotConfiguredException(), "Policy search is not configured.");

    [Fact]
    public async Task Ask_unavailable_returns_503_with_the_exact_detail() =>
        await AssertAskFailsAsync(new PolicySearchUnavailableException(new HttpRequestException("down")), "Policy search is temporarily unavailable.");

    [Fact]
    public async Task Ask_not_indexed_returns_503_with_the_exact_detail() =>
        await AssertAskFailsAsync(new PoliciesNotIndexedException(), "Policies have not been indexed yet.");

    private async Task AssertAskFailsAsync(Exception error, string detail)
    {
        Factory.Policies.OnAsk = _ => throw error;
        var (fakes, client) = await CustomerWithFakesAsync();
        using (fakes) using (client)
        {
            var response = await client.PostAsJsonAsync(AskUrl, new AskPolicyRequest("Can I cancel?"), Ct);

            var problem = await Problem.AssertAsync(response, HttpStatusCode.ServiceUnavailable);
            problem.GetProperty("detail").GetString().ShouldBe(detail);
        }
    }

    [Fact]
    public async Task Reindex_without_a_token_returns_401_and_with_a_customer_token_403()
    {
        var anonymous = await Client.PostAsync(ReindexUrl, null, Ct);
        await Problem.AssertAsync(anonymous, HttpStatusCode.Unauthorized);

        var customer = await Api.RegisterCustomerAsync();
        var forbidden = await customer.Client.PostAsync(ReindexUrl, null, Ct);
        await Problem.AssertAsync(forbidden, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reindex_as_admin_returns_the_counts()
    {
        Factory.Policies.OnReindex = () => new ReindexResult(3, 1, 5, 2, 9);
        using var fakes = Factory.WithFakePolicies();
        using var client = ClientFor(fakes, await AdminTokenAsync());

        var response = await client.PostAsync(ReindexUrl, null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        body.GetProperty("added").GetInt32().ShouldBe(3);
        body.GetProperty("updated").GetInt32().ShouldBe(1);
        body.GetProperty("unchanged").GetInt32().ShouldBe(5);
        body.GetProperty("deleted").GetInt32().ShouldBe(2);
        body.GetProperty("totalChunks").GetInt32().ShouldBe(9);
    }

    [Fact]
    public async Task Reindex_unavailable_returns_503()
    {
        Factory.Policies.OnReindex = () => throw new PolicySearchUnavailableException(new HttpRequestException("down"));
        using var fakes = Factory.WithFakePolicies();
        using var client = ClientFor(fakes, await AdminTokenAsync());

        var response = await client.PostAsync(ReindexUrl, null, Ct);

        await Problem.AssertAsync(response, HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Policy_503s_are_logged_at_warning_with_type_names_only()
    {
        var logs = new CapturingLoggerProvider();
        var (fakes, client) = await CustomerWithFakesAsync(logs.For<GlobalExceptionHandler>());
        using (fakes) using (client)
        {
            Exception[] errors =
            [
                new PolicySearchNotConfiguredException(),
                new PolicySearchUnavailableException(new HttpRequestException("inner-secret-detail")),
                new PoliciesNotIndexedException(),
            ];

            foreach (var error in errors)
            {
                Factory.Policies.OnAsk = _ => throw error;
                var response = await client.PostAsJsonAsync(AskUrl, new AskPolicyRequest("Can I cancel?"), Ct);
                await Problem.AssertAsync(response, HttpStatusCode.ServiceUnavailable);
            }
        }

        var warnings = logs.Entries.Where(e => e.Level == LogLevel.Warning && e.Category.EndsWith("GlobalExceptionHandler")).ToList();
        warnings.Count.ShouldBe(3);
        warnings.Select(w => w.Message).ShouldContain(m => m.Contains("PolicySearchNotConfiguredException"));
        warnings.Select(w => w.Message).ShouldContain(m => m.Contains("PolicySearchUnavailableException") && m.Contains("HttpRequestException"));
        warnings.Select(w => w.Message).ShouldContain(m => m.Contains("PoliciesNotIndexedException"));
        logs.Entries.ShouldAllBe(e => !e.Message.Contains("inner-secret-detail") && (e.ExceptionText == null || !e.ExceptionText.Contains("inner-secret-detail")));
    }

    [Fact]
    public async Task Policy_services_resolve_from_the_real_container()
    {
        Factory.Services.GetRequiredService<IPolicyIndexer>().ShouldBeOfType<PolicyIndexer>();
        Factory.Services.GetRequiredService<IPolicyRetriever>().ShouldBeOfType<PolicyRetriever>();
        Factory.Services.GetRequiredService<IPolicyQuestionService>().ShouldBeOfType<PolicyQuestionService>();

        // Testing is neither Development nor Docker, so the startup indexer returns at once without touching Qdrant.
        var startup = Factory.Services.GetServices<IHostedService>().OfType<PolicyIndexingHostedService>().Single();
        startup.ExecuteTask.ShouldNotBeNull().IsCompletedSuccessfully.ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Ask_with_the_real_services_and_no_embedding_endpoint_returns_503_not_configured()
    {
        var customer = await Api.RegisterCustomerAsync();

        var response = await customer.Client.PostAsJsonAsync(AskUrl, new AskPolicyRequest("Can I cancel my order?"), Ct);

        var problem = await Problem.AssertAsync(response, HttpStatusCode.ServiceUnavailable);
        problem.GetProperty("detail").GetString().ShouldBe("Policy search is not configured.");
    }
}
