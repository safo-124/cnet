using MepCatalog.Core;
using Microsoft.EntityFrameworkCore;

namespace MepCatalog.Data;

public record ImportRowError(int RowNumber, string? Manufacturer, string? Model, IReadOnlyList<string> Errors);

public record ImportReport(int Created, int Updated, IReadOnlyList<ImportRowError> Failed)
{
    public int Total => Created + Updated + Failed.Count;
}

/// <summary>
/// Normalizes raw manufacturer rows and upserts them into the catalog, keyed on manufacturer + model.
/// Bad rows are reported and skipped; good rows are still imported.
/// </summary>
public class ProductImportService(CatalogDbContext db)
{
    public async Task<ImportReport> ImportAsync(IEnumerable<RawProductRow> rows, CancellationToken ct = default)
    {
        var created = 0;
        var updated = 0;
        var failed = new List<ImportRowError>();
        var seenInFile = new Dictionary<(string, string), Product>();

        // Row 1 is the CSV header, so data starts at row 2.
        var rowNumber = 1;
        foreach (var row in rows)
        {
            rowNumber++;
            var result = ProductNormalizer.Normalize(row);
            if (!result.Success)
            {
                failed.Add(new ImportRowError(rowNumber, row.Manufacturer, row.Model, result.Errors));
                continue;
            }

            var incoming = result.Product!;
            var key = (incoming.Manufacturer.ToUpperInvariant(), incoming.Model);

            var existing = seenInFile.GetValueOrDefault(key)
                ?? await db.Products.FirstOrDefaultAsync(
                    p => p.Manufacturer.ToUpper() == key.Item1 && p.Model == incoming.Model, ct);

            if (existing is null)
            {
                db.Products.Add(incoming);
                seenInFile[key] = incoming;
                created++;
            }
            else
            {
                existing.Category = incoming.Category;
                existing.Description = incoming.Description ?? existing.Description;
                existing.AirflowLps = incoming.AirflowLps ?? existing.AirflowLps;
                existing.PowerW = incoming.PowerW ?? existing.PowerW;
                existing.ConnectionSizeMm = incoming.ConnectionSizeMm ?? existing.ConnectionSizeMm;
                existing.WeightKg = incoming.WeightKg ?? existing.WeightKg;
                // A repeated row in the same file also counts as an update of the earlier row.
                seenInFile[key] = existing;
                updated++;
            }
        }

        await db.SaveChangesAsync(ct);
        return new ImportReport(created, updated, failed);
    }
}
