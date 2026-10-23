using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using OrderManagement.Application.Auth;
using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;
using OrderManagement.Infrastructure.Security;

namespace OrderManagement.Infrastructure.Persistence;

/// <summary>
/// Seeds in C# rather than SQL because the admin password is hashed and comes from configuration.
/// </summary>
public class DatabaseSeeder(
    AppDbContext context,
    IPasswordHasher hasher,
    IOptions<SeedOptions> options,
    TimeProvider timeProvider)
{
    private static readonly (string Sku, string Name, string Description, decimal Price)[] SampleProducts =
    [
        ("SAMPLE-001", "Wireless Mouse", "Ergonomic 2.4 GHz wireless mouse", 24.99m),
        ("SAMPLE-002", "Mechanical Keyboard", "Tenkeyless keyboard with brown switches", 89.90m),
        ("SAMPLE-003", "USB-C Hub", "7-in-1 USB-C hub with HDMI and card reader", 39.50m),
        ("SAMPLE-004", "27-inch Monitor", "27-inch 1440p IPS monitor", 279.00m),
        ("SAMPLE-005", "Laptop Stand", "Adjustable aluminium laptop stand", 34.95m)
    ];

    public async Task SeedAsync(CancellationToken ct)
    {
        SeedOptions seed = options.Value;
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;

        string adminEmail = User.NormalizeEmail(seed.AdminEmail);
        if (!await context.Users.AnyAsync(u => u.Email == adminEmail, ct))
        {
            User admin = User.Create(seed.AdminEmail, UserRole.Admin, now);
            admin.SetPasswordHash(hasher.Hash(admin, seed.AdminPassword));
            context.Users.Add(admin);
        }

        if (!await context.Products.AnyAsync(ct))
        {
            foreach ((string sku, string name, string description, decimal price) in SampleProducts)
                context.Products.Add(Product.Create(sku, name, description, price, 100, now));
        }

        await context.SaveChangesAsync(ct);
    }
}
