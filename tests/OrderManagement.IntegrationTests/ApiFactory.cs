using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using OrderManagement.Api.Infrastructure;
using OrderManagement.Application.Assistant;
using OrderManagement.Application.Policies;
using OrderManagement.Infrastructure.Assistant;
using OrderManagement.Infrastructure.Persistence;

namespace OrderManagement.IntegrationTests;

/// <summary>
/// Hosts the API in-process against its own MySQL container (started and migrated once per collection).
/// The environment is <c>Testing</c>, so the app itself does not migrate or seed; tests do that through
/// <see cref="ResetDatabaseAsync"/>.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Fixed 48-character test signing key, public so tests can mint their own tokens.</summary>
    public const string JwtSigningKey = "test-signing-key-0123456789-abcdefghijklmnopqrst";
    public const string JwtIssuer = "OrderManagement";
    public const string JwtAudience = "OrderManagement.Clients";
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "Admin12345";

    private readonly MySqlFixture _database = new();

    public MySqlFixture Database => _database;

    /// <summary>The fake Claude used by every API test. Reset before each test by <see cref="IntegrationTestBase"/>.</summary>
    public ScriptedAssistantModel Assistant { get; } = new();

    /// <summary>
    /// Scriptable policy question service and indexer, used only by factories made with <see cref="WithFakePolicies"/>.
    /// The base factory keeps the real policy services. Reset before each test by <see cref="IntegrationTestBase"/>.
    /// </summary>
    public FakePolicyServices Policies { get; } = new();

    /// <summary>A factory whose policy question service and indexer are <see cref="Policies"/>, so no Qdrant or Ollama is needed.</summary>
    public WebApplicationFactory<Program> WithFakePolicies(ILogger<GlobalExceptionHandler>? handlerLogger = null) =>
        WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddSingleton<IPolicyQuestionService>(Policies);
            s.AddSingleton<IPolicyIndexer>(Policies);
            if (handlerLogger is not null) s.AddSingleton(handlerLogger);
        }));

    /// <summary>A factory that uses the real Anthropic adapter (with no API key), for testing the not-configured path.</summary>
    public WebApplicationFactory<Program> WithRealAssistantModel(ILogger<GlobalExceptionHandler>? handlerLogger = null) =>
        WithWebHostBuilder(b =>
        {
            if (handlerLogger is not null) b.ConfigureTestServices(s => s.AddSingleton(handlerLogger));
            b.ConfigureTestServices(s =>
                s.AddSingleton<IAssistantModel>(sp => new ChatClientAssistantModel(sp.GetRequiredService<ChatClientFactory>().Create, 4096)));
        });

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }

    /// <summary>Empties every application table, then runs the seeder (admin user and sample products).</summary>
    public async Task ResetDatabaseAsync(CancellationToken ct = default)
    {
        await _database.ResetAsync();
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync(ct);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _database.ConnectionString,
                ["Jwt:Issuer"] = JwtIssuer,
                ["Jwt:Audience"] = JwtAudience,
                ["Jwt:SigningKey"] = JwtSigningKey,
                ["Seed:AdminEmail"] = AdminEmail,
                ["Seed:AdminPassword"] = AdminPassword,
                // Environment variables still load in the Testing environment, so a developer's real provider
                // settings must not reach the API from tests: pin the provider and blank every key and endpoint.
                ["Assistant:Provider"] = "Claude",
                ["ANTHROPIC_API_KEY"] = "",
                ["ANTHROPIC_BASE_URL"] = "",
                ["OPENAI_API_KEY"] = "",
                ["GEMINI_API_KEY"] = "",
                ["Assistant:OpenAI:Endpoint"] = "",
                ["Assistant:Ollama:Endpoint"] = "",
                // Same for policy search: no embedding host and no Qdrant key from a developer's environment.
                ["Policies:Embedding:Endpoint"] = "",
                ["Policies:Qdrant:ApiKey"] = ""
            });
        });
        builder.ConfigureTestServices(s => s.AddSingleton<IAssistantModel>(Assistant));
    }
}
