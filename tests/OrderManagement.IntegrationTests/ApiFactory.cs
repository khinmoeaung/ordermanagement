using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
                ["Seed:AdminPassword"] = AdminPassword
            });
        });
    }
}
