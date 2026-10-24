using DbUp.Engine;

using Microsoft.EntityFrameworkCore;

using MySql.Data.MySqlClient;

using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;
using OrderManagement.Infrastructure.Persistence;

using Shouldly;

namespace OrderManagement.IntegrationTests.Persistence;

[Collection("Persistence")]
public sealed class SchemaTests(MySqlFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 23, 12, 30, 45, 123, DateTimeKind.Utc);

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public void Migrator_on_already_migrated_database_runs_no_scripts()
    {
        DatabaseUpgradeResult result = DatabaseMigrator.Migrate(fixture.ConnectionString);

        result.Successful.ShouldBeTrue();
        result.Scripts.ShouldBeEmpty();
    }

    [Fact]
    public async Task Check_constraint_rejects_negative_stock()
    {
        await using AppDbContext db = fixture.CreateDbContext();

        MySqlException ex = await Should.ThrowAsync<MySqlException>(() => db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO products (id, sku, name, description, price, stock_quantity, is_active, created_at, updated_at)
            VALUES (UUID(), 'SKU-NEG', 'Negative', NULL, 10.00, -1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
            """,
            TestContext.Current.CancellationToken));

        ex.Message.ShouldContain("ck_products_stock_quantity");
    }

    [Fact]
    public async Task Product_round_trips_through_generated_model()
    {
        Product product = NewProduct("SKU-RT", stock: 7);
        await SaveAsync(product);

        await using AppDbContext db = fixture.CreateDbContext();
        Product loaded = await db.Products.SingleAsync(p => p.Id == product.Id, TestContext.Current.CancellationToken);

        loaded.Id.ShouldBe(product.Id);
        loaded.Sku.ShouldBe("SKU-RT");
        loaded.Name.ShouldBe("Round trip");
        loaded.Description.ShouldBeNull();
        loaded.Price.ShouldBe(19.99m);
        loaded.StockQuantity.ShouldBe(7);
        loaded.IsActive.ShouldBeTrue();
        loaded.CreatedAt.ShouldBe(Now);
        loaded.UpdatedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task Order_status_is_stored_as_string()
    {
        Product product = NewProduct("SKU-ST", stock: 10);
        Order order = NewOrder(NewCustomer("status@example.com"), product, OrderStatus.Confirmed);
        await SaveAsync(product, order);

        await using AppDbContext db = fixture.CreateDbContext();
        string stored = await db.Database
            .SqlQuery<string>($"SELECT status AS `Value` FROM orders WHERE id = {order.Id.ToString()}")
            .SingleAsync(TestContext.Current.CancellationToken);
        Order loaded = await db.Orders.SingleAsync(o => o.Id == order.Id, TestContext.Current.CancellationToken);

        stored.ShouldBe("Confirmed");
        loaded.Status.ShouldBe(OrderStatus.Confirmed);
        loaded.CustomerId.ShouldBe(order.CustomerId);
    }

    [Fact]
    public async Task Stale_stock_update_throws_concurrency_exception()
    {
        Product product = NewProduct("SKU-CC", stock: 10);
        await SaveAsync(product);
        CancellationToken ct = TestContext.Current.CancellationToken;

        await using AppDbContext first = fixture.CreateDbContext();
        await using AppDbContext second = fixture.CreateDbContext();
        Product firstCopy = await first.Products.SingleAsync(p => p.Id == product.Id, ct);
        Product secondCopy = await second.Products.SingleAsync(p => p.Id == product.Id, ct);

        firstCopy.StockQuantity = 8;
        await first.SaveChangesAsync(ct);

        secondCopy.StockQuantity = 9;
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Deleting_order_cascades_to_items_but_product_is_restricted()
    {
        Product product = NewProduct("SKU-DEL", stock: 10);
        Order order = NewOrder(NewCustomer("delete@example.com"), product, OrderStatus.Pending);
        await SaveAsync(product, order);
        CancellationToken ct = TestContext.Current.CancellationToken;

        await using (AppDbContext db = fixture.CreateDbContext())
        {
            db.Products.Remove(await db.Products.SingleAsync(p => p.Id == product.Id, ct));
            await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
        }

        await using (AppDbContext db = fixture.CreateDbContext())
        {
            db.Orders.Remove(await db.Orders.SingleAsync(o => o.Id == order.Id, ct));
            await db.SaveChangesAsync(ct);
        }

        await using (AppDbContext db = fixture.CreateDbContext())
        {
            (await db.OrderItems.CountAsync(i => i.OrderId == order.Id, ct)).ShouldBe(0);
            (await db.Products.AnyAsync(p => p.Id == product.Id, ct)).ShouldBeTrue();
        }
    }

    private async Task SaveAsync(params object[] entities)
    {
        await using AppDbContext db = fixture.CreateDbContext();
        db.AddRange(entities);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static Product NewProduct(string sku, int stock) => new()
    {
        Id = Guid.CreateVersion7(),
        Sku = sku,
        Name = "Round trip",
        Description = null,
        Price = 19.99m,
        StockQuantity = stock,
        IsActive = true,
        CreatedAt = Now,
        UpdatedAt = Now
    };

    private static Customer NewCustomer(string email) => new()
    {
        Id = Guid.CreateVersion7(),
        FullName = "Test Customer",
        Phone = "+10000000000",
        ShippingAddress = "1 Test Street",
        User = new User
        {
            Id = Guid.CreateVersion7(),
            Email = email,
            PasswordHash = "hash",
            Role = UserRole.Customer,
            CreatedAt = Now
        }
    };

    private static Order NewOrder(Customer customer, Product product, OrderStatus status)
    {
        Guid orderId = Guid.CreateVersion7();
        return new Order
        {
            Id = orderId,
            OrderNumber = $"ORD-20261023-{orderId.ToString("N")[^6..].ToUpperInvariant()}",
            Customer = customer,
            Status = status,
            TotalAmount = 39.98m,
            CreatedAt = Now,
            UpdatedAt = Now,
            OrderItems =
            [
                new OrderItem
                {
                    Id = Guid.CreateVersion7(),
                    Product = product,
                    ProductName = product.Name,
                    UnitPrice = product.Price,
                    Quantity = 2,
                    LineTotal = 39.98m
                }
            ]
        };
    }
}
