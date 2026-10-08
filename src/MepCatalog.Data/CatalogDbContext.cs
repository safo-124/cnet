using MepCatalog.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MepCatalog.Data;

public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<AuditRun> AuditRuns => Set<AuditRun>();

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

        modelBuilder.Entity<AuditRun>(run =>
        {
            run.Property(r => r.ProjectGlobalId).HasMaxLength(22); // IFC GlobalIds are 22 characters
            run.Property(r => r.ProjectName).HasMaxLength(200);
            run.Property(r => r.FileName).HasMaxLength(260);
            run.HasIndex(r => new { r.ProjectGlobalId, r.AuditedUtc });
        });

        // SQLite has no time zones, so dates come back as DateTimeKind.Unspecified and would be sent to browsers
        // without the "Z" that marks UTC, which shows them hours off in any other time zone. Every *Utc
        // property is UTC by convention, so mark it as such on the way out of the database.
        var utc = new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
        {
            if (property.ClrType == typeof(DateTime) && property.Name.EndsWith("Utc", StringComparison.Ordinal))
                property.SetValueConverter(utc);
        }
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
