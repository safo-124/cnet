using MepCatalog.Core;
using Microsoft.EntityFrameworkCore;

namespace MepCatalog.Data;

public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(product =>
        {
            product.Property(p => p.Manufacturer).HasMaxLength(100);
            product.Property(p => p.Model).HasMaxLength(100);
            product.Property(p => p.Description).HasMaxLength(500);
            product.Property(p => p.Category).HasConversion<string>().HasMaxLength(50);
            product.HasIndex(p => new { p.Manufacturer, p.Model }).IsUnique();
            product.HasIndex(p => p.Category);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Product>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.CreatedUtc = now;
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Entity.UpdatedUtc = now;
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
