using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using OrderManagement.Application.Common;
using OrderManagement.Application.Customers;
using OrderManagement.Application.Orders;
using OrderManagement.Application.Products;
using OrderManagement.Domain.Common;
using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;
using OrderManagement.Infrastructure.Persistence;
using OrderManagement.Infrastructure.Persistence.Repositories;
using OrderManagement.Infrastructure.Security;

using Shouldly;

namespace OrderManagement.IntegrationTests.Persistence;

[Collection("Persistence")]
public sealed class PersistenceTests(MySqlFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 23, 12, 30, 45, 123, DateTimeKind.Utc);

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Duplicate_email_save_throws_conflict()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using (AppDbContext first = fixture.CreateDbContext())
        {
            new UserRepository(first).Add(NewUser("dup@example.com"));
            await ((IUnitOfWork)first).SaveChangesAsync(ct);
        }

        await using AppDbContext second = fixture.CreateDbContext();
        new UserRepository(second).Add(NewUser("dup@example.com"));

        ConflictException ex = await Should.ThrowAsync<ConflictException>(
            () => ((IUnitOfWork)second).SaveChangesAsync(ct));
        ex.Message.ShouldBe("A record with the same unique value already exists.");
    }

    [Fact]
    public async Task Stale_stock_update_throws_conflict()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Product product = Product.Create("SKU-STALE", "Stale", null, 10m, 10, Now);
        await using (AppDbContext seed = fixture.CreateDbContext())
        {
            new ProductRepository(seed).Add(product);
            await ((IUnitOfWork)seed).SaveChangesAsync(ct);
        }

        await using AppDbContext first = fixture.CreateDbContext();
        await using AppDbContext second = fixture.CreateDbContext();
        Product firstCopy = (await new ProductRepository(first).GetByIdAsync(product.Id, ct))!;
        Product secondCopy = (await new ProductRepository(second).GetByIdAsync(product.Id, ct))!;

        firstCopy.DeductStock(1, Now);
        await ((IUnitOfWork)first).SaveChangesAsync(ct);

        secondCopy.DeductStock(2, Now);
        ConflictException ex = await Should.ThrowAsync<ConflictException>(
            () => ((IUnitOfWork)second).SaveChangesAsync(ct));
        ex.Message.ShouldBe("The data was changed by another request. Please retry.");
    }

    [Fact]
    public async Task Concurrent_cancel_and_ship_throws_conflict()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Product product = Product.Create("SKU-RACE", "Race", null, 10m, 10, Now);
        Customer customer = NewCustomer("race@example.com");
        Order order = Order.Create(customer.Id, [new OrderLine(product, 1)], Now);
        order.Confirm(Now);
        await using (AppDbContext seed = fixture.CreateDbContext())
        {
            new ProductRepository(seed).Add(product);
            new CustomerRepository(seed).Add(customer);
            new OrderRepository(seed).Add(order);
            await ((IUnitOfWork)seed).SaveChangesAsync(ct);
        }

        await using AppDbContext first = fixture.CreateDbContext();
        await using AppDbContext second = fixture.CreateDbContext();
        Order cancelCopy = (await new OrderRepository(first).GetByIdAsync(order.Id, ct))!;
        Order shipCopy = (await new OrderRepository(second).GetByIdAsync(order.Id, ct))!;

        cancelCopy.Cancel(Now);
        await ((IUnitOfWork)first).SaveChangesAsync(ct);

        shipCopy.Ship(Now);
        await Should.ThrowAsync<ConflictException>(() => ((IUnitOfWork)second).SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Concurrent_refresh_token_revoke_throws_conflict()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        User user = NewUser("tokenrace@example.com");
        RefreshToken token = RefreshToken.Create(user.Id, "race-hash", Now.AddDays(1), Now);
        await using (AppDbContext seed = fixture.CreateDbContext())
        {
            new UserRepository(seed).Add(user);
            new RefreshTokenRepository(seed).Add(token);
            await ((IUnitOfWork)seed).SaveChangesAsync(ct);
        }

        await using AppDbContext first = fixture.CreateDbContext();
        await using AppDbContext second = fixture.CreateDbContext();
        RefreshToken a = (await new RefreshTokenRepository(first).GetByHashAsync("race-hash", ct))!;
        RefreshToken b = (await new RefreshTokenRepository(second).GetByHashAsync("race-hash", ct))!;

        a.Revoke(Now);
        await ((IUnitOfWork)first).SaveChangesAsync(ct);

        b.Revoke(Now);
        await Should.ThrowAsync<ConflictException>(() => ((IUnitOfWork)second).SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Order_with_items_round_trips_via_repository()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Product product = Product.Create("SKU-ORD", "Widget", null, 19.99m, 50, Now);
        Customer customer = NewCustomer("order@example.com");
        Order order = Order.Create(customer.Id, [new OrderLine(product, 3)], Now);

        await using (AppDbContext db = fixture.CreateDbContext())
        {
            new ProductRepository(db).Add(product);
            new CustomerRepository(db).Add(customer);
            new OrderRepository(db).Add(order);
            await ((IUnitOfWork)db).SaveChangesAsync(ct);
        }

        await using AppDbContext read = fixture.CreateDbContext();
        Order loaded = (await new OrderRepository(read).GetByIdAsync(order.Id, ct))!;

        loaded.OrderNumber.ShouldBe(order.OrderNumber);
        loaded.TotalAmount.ShouldBe(59.97m);
        loaded.OrderItems.Count.ShouldBe(1);
        OrderItem item = loaded.OrderItems.Single();
        item.UnitPrice.ShouldBe(19.99m);
        item.LineTotal.ShouldBe(59.97m);
        item.Quantity.ShouldBe(3);

        PagedResult<Order> page = await new OrderRepository(read).ListAsync(
            new OrderListQuery { Status = OrderStatus.Pending }, customer.Id, ct);
        page.TotalCount.ShouldBe(1);
        page.Items.Single().OrderItems.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Product_list_search_and_paging_work()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Product inactive = Product.Create("CCC-4", "Delta Lamp", null, 5m, 1, Now);
        inactive.Deactivate(Now);
        await using (AppDbContext db = fixture.CreateDbContext())
        {
            ProductRepository repo = new(db);
            repo.Add(Product.Create("AAA-1", "Alpha Lamp", null, 5m, 1, Now));
            repo.Add(Product.Create("BBB-2", "Beta Desk", null, 5m, 1, Now));
            repo.Add(Product.Create("LAMP-3", "Gamma", null, 5m, 1, Now));
            repo.Add(inactive);
            await ((IUnitOfWork)db).SaveChangesAsync(ct);
        }

        await using AppDbContext read = fixture.CreateDbContext();
        ProductRepository reader = new(read);

        PagedResult<Product> search = await reader.ListAsync(new ProductListQuery { Search = "lamp" }, false, ct);
        search.TotalCount.ShouldBe(2);
        search.Items.Select(p => p.Sku).ShouldBe(["AAA-1", "LAMP-3"]);

        PagedResult<Product> all = await reader.ListAsync(new ProductListQuery { Search = "  " }, true, ct);
        all.TotalCount.ShouldBe(4);

        PagedResult<Product> paged = await reader.ListAsync(new ProductListQuery { Page = 2, PageSize = 2 }, true, ct);
        paged.TotalCount.ShouldBe(4);
        paged.Items.Select(p => p.Name).ShouldBe(["Delta Lamp", "Gamma"]);

        (await reader.GetByIdsAsync([inactive.Id], ct)).Count.ShouldBe(1);
        (await reader.SkuExistsAsync("AAA-1", null, ct)).ShouldBeTrue();
        Guid alphaId = all.Items.Single(p => p.Sku == "AAA-1").Id;
        (await reader.SkuExistsAsync("AAA-1", alphaId, ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Customer_list_search_matches_email()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using (AppDbContext db = fixture.CreateDbContext())
        {
            CustomerRepository repo = new(db);
            repo.Add(NewCustomer("alice@example.com", "Alice Smith"));
            repo.Add(NewCustomer("bob@sample.org", "Bob Jones"));
            await ((IUnitOfWork)db).SaveChangesAsync(ct);
        }

        await using AppDbContext read = fixture.CreateDbContext();
        CustomerRepository reader = new(read);

        PagedResult<Customer> byEmail = await reader.ListAsync(new CustomerListQuery { Search = "sample.org" }, ct);
        byEmail.TotalCount.ShouldBe(1);
        byEmail.Items.Single().User.Email.ShouldBe("bob@sample.org");

        PagedResult<Customer> byName = await reader.ListAsync(new CustomerListQuery { Search = "alice" }, ct);
        byName.Items.Single().FullName.ShouldBe("Alice Smith");

        (await reader.ListAsync(new CustomerListQuery(), ct)).TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task Seed_is_idempotent()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        for (int i = 0; i < 2; i++)
        {
            await using AppDbContext db = fixture.CreateDbContext();
            DatabaseSeeder seeder = new(
                db,
                new AspNetPasswordHasher(),
                Options.Create(new SeedOptions { AdminEmail = "Admin@Example.com", AdminPassword = "Passw0rd-seed" }),
                TimeProvider.System);
            await seeder.SeedAsync(ct);
        }

        await using AppDbContext read = fixture.CreateDbContext();
        (await read.Users.CountAsync(ct)).ShouldBe(1);
        User admin = await read.Users.SingleAsync(ct);
        admin.Email.ShouldBe("admin@example.com");
        admin.Role.ShouldBe(UserRole.Admin);
        (await read.Products.CountAsync(ct)).ShouldBe(5);
        (await read.Products.CountAsync(p => p.Sku.StartsWith("SAMPLE-"), ct)).ShouldBe(5);
    }

    [Fact]
    public async Task DateTimes_read_back_as_utc()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Product product = Product.Create("SKU-UTC", "Utc", null, 1m, 1, Now);
        User user = NewUser("utc@example.com");
        RefreshToken token = RefreshToken.Create(user.Id, "h", Now.AddDays(1), Now);
        await using (AppDbContext db = fixture.CreateDbContext())
        {
            new ProductRepository(db).Add(product);
            new UserRepository(db).Add(user);
            new RefreshTokenRepository(db).Add(token);
            await ((IUnitOfWork)db).SaveChangesAsync(ct);
        }

        await using AppDbContext read = fixture.CreateDbContext();
        Product loaded = (await new ProductRepository(read).GetByIdAsync(product.Id, ct))!;
        loaded.CreatedAt.Kind.ShouldBe(DateTimeKind.Utc);
        loaded.UpdatedAt.Kind.ShouldBe(DateTimeKind.Utc);
        loaded.CreatedAt.ShouldBe(Now);

        RefreshTokenRepository tokens = new(read);
        RefreshToken t = (await tokens.GetByHashAsync("h", ct))!;
        t.ExpiresAt.Kind.ShouldBe(DateTimeKind.Utc);
        t.RevokedAt.ShouldBeNull();
        (await tokens.GetActiveByUserIdAsync(user.Id, Now, ct)).Count.ShouldBe(1);
        (await tokens.GetActiveByUserIdAsync(user.Id, Now.AddDays(2), ct)).Count.ShouldBe(0);
    }

    private static User NewUser(string email)
    {
        User user = User.Create(email, UserRole.Customer, Now);
        user.SetPasswordHash("hash");
        return user;
    }

    private static Customer NewCustomer(string email, string fullName = "Test Customer") =>
        Customer.Create(NewUser(email), fullName, "+10000000000", "1 Test Street");
}
