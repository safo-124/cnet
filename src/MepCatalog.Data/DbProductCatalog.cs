using MepCatalog.Core;
using MepCatalog.Core.Auditing;
using Microsoft.EntityFrameworkCore;

namespace MepCatalog.Data;

/// <summary>Catalog lookup straight from the database. Manufacturer matching is case-insensitive; models are stored upper-case.</summary>
public class DbProductCatalog(CatalogDbContext db) : IProductCatalog
{
    public Task<Product?> FindAsync(string manufacturer, string model, CancellationToken ct = default)
    {
        var m = manufacturer.Trim().ToUpper();
        var code = model.Trim().ToUpperInvariant();
        return db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Manufacturer.ToUpper() == m && p.Model == code, ct);
    }
}
