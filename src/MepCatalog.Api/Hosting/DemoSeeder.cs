using MepCatalog.Data;
using Microsoft.EntityFrameworkCore;

namespace MepCatalog.Api;

/// <summary>On the public demo, fills an empty catalog with the sample products so visitors see a working app.</summary>
public static class DemoSeeder
{
    public static async Task SeedIfEmptyAsync(IServiceProvider services, string csvPath, ILogger logger)
    {
        var db = services.GetRequiredService<CatalogDbContext>();
        if (await db.Products.AnyAsync())
            return;

        if (!File.Exists(csvPath))
        {
            logger.LogWarning("Demo seed file {Path} not found; starting with an empty catalog", csvPath);
            return;
        }

        using var reader = new StreamReader(csvPath);
        var report = await services.GetRequiredService<ProductImportService>().ImportAsync(CsvProductReader.Read(reader));
        logger.LogInformation("Seeded demo catalog: {Created} products ({Failed} sample rows rejected on purpose)",
            report.Created, report.Failed.Count);
    }
}
