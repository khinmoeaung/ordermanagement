using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using MySql.Data.MySqlClient;

using OrderManagement.Application.Common;
using OrderManagement.Domain.Common;
using OrderManagement.Domain.Entities;

namespace OrderManagement.Infrastructure.Persistence;

// Hand-written configuration that scaffolding never overwrites (spec §2a).
public partial class AppDbContext : IUnitOfWork
{
    private const int MySqlDuplicateEntry = 1062;

    // Timestamps are stored as UTC: stamp Kind=Utc on read, and normalize Local values on write.
    private static readonly ValueConverter<DateTime, DateTime> UtcConverter = new(
        v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v,
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private static readonly ValueConverter<DateTime?, DateTime?> NullableUtcConverter = new(
        v => v.HasValue && v.Value.Kind == DateTimeKind.Local ? v.Value.ToUniversalTime() : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(e => e.StockQuantity).IsConcurrencyToken();
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(e => e.Role).HasConversion<string>();
        });

        modelBuilder.Entity<RefreshToken>().Property(e => e.RevokedAt).IsConcurrencyToken();

        modelBuilder.Entity<Order>(entity =>
        {
            entity.Property(e => e.Status).HasConversion<string>();
            entity.Property(e => e.Status).IsConcurrencyToken();
        });

        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (IMutableProperty property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                    property.SetValueConverter(UtcConverter);
                else if (property.ClrType == typeof(DateTime?))
                    property.SetValueConverter(NullableUtcConverter);
            }
        }
    }

    // Explicit because DbContext.SaveChangesAsync returns Task<int>.
    async Task IUnitOfWork.SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The data was changed by another request. Please retry.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { Number: MySqlDuplicateEntry })
        {
            throw new ConflictException("A record with the same unique value already exists.");
        }
    }
}
