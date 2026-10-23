using Microsoft.EntityFrameworkCore;

using OrderManagement.Domain.Entities;

namespace OrderManagement.Infrastructure.Persistence;

// Hand-written configuration that scaffolding never overwrites (spec §2a).
public partial class AppDbContext
{
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

        modelBuilder.Entity<Order>(entity =>
        {
            entity.Property(e => e.Status).HasConversion<string>();
        });
    }
}
