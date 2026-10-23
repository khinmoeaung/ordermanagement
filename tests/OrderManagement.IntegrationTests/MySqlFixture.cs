using Microsoft.EntityFrameworkCore;

using OrderManagement.Infrastructure.Persistence;

using Testcontainers.MySql;

namespace OrderManagement.IntegrationTests;

/// <summary>
/// Starts one throwaway MySQL 8.4 container and applies the DbUp migrations once.
/// Tests call <see cref="ResetAsync"/> to start from empty tables.
/// </summary>
public sealed class MySqlFixture : IAsyncLifetime
{
    // The DbUp journal (schemaversions) is deliberately not truncated.
    private const string TruncateAllTables =
        """
        SET FOREIGN_KEY_CHECKS = 0;
        TRUNCATE TABLE order_items;
        TRUNCATE TABLE orders;
        TRUNCATE TABLE refresh_tokens;
        TRUNCATE TABLE customers;
        TRUNCATE TABLE users;
        TRUNCATE TABLE products;
        SET FOREIGN_KEY_CHECKS = 1;
        """;

    // Testcontainers 4.16 obsoletes the parameterless MySqlBuilder(); the image goes in the constructor.
    private readonly MySqlContainer _container = new MySqlBuilder("mysql:8.4").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        DatabaseMigrator.Migrate(ConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public AppDbContext CreateDbContext()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySQL(ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>Empties the six application tables. The DbUp <c>schemaversions</c> journal is kept.</summary>
    public async Task ResetAsync()
    {
        await using AppDbContext db = CreateDbContext();
        await db.Database.ExecuteSqlRawAsync(TruncateAllTables);
    }
}

[CollectionDefinition("Persistence")]
public sealed class PersistenceCollection : ICollectionFixture<MySqlFixture>;
