using MepCatalog.Data;
using Microsoft.EntityFrameworkCore;

// Usage: MepCatalog.Importer <file.csv> [--db <path-to-sqlite-file>]
if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage: MepCatalog.Importer <file.csv> [--db <path-to-sqlite-file>]");
    return args.Length == 0 ? 1 : 0;
}

var csvPath = args[0];
var dbIndex = Array.IndexOf(args, "--db");
var dbPath = dbIndex >= 0 && dbIndex + 1 < args.Length ? args[dbIndex + 1] : "mepcatalog.db";

if (!File.Exists(csvPath))
{
    Console.Error.WriteLine($"File not found: {csvPath}");
    return 1;
}

var options = new DbContextOptionsBuilder<CatalogDbContext>()
    .UseSqlite($"Data Source={dbPath}")
    .Options;

await using var db = new CatalogDbContext(options);
await db.Database.MigrateAsync();

using var reader = new StreamReader(csvPath);
var rows = CsvProductReader.Read(reader);
var report = await new ProductImportService(db).ImportAsync(rows);

Console.WriteLine($"Read {report.Total} rows from {Path.GetFileName(csvPath)} into {Path.GetFullPath(dbPath)}");
Console.WriteLine($"  Created: {report.Created}");
Console.WriteLine($"  Updated: {report.Updated}");
Console.WriteLine($"  Failed:  {report.Failed.Count}");

foreach (var error in report.Failed)
{
    Console.WriteLine($"  Row {error.RowNumber} ({error.Manufacturer} {error.Model}):");
    foreach (var message in error.Errors)
        Console.WriteLine($"    - {message}");
}

return report.Failed.Count == 0 ? 0 : 2;
