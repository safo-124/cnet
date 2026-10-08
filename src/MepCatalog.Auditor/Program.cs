using MepCatalog.Client;
using MepCatalog.Core.Auditing;
using MepCatalog.Ifc;
using MepCatalog.Reporting;
using Xbim.Ifc;

const string Usage = """
    Usage:
      MepCatalog.Auditor sample <out.ifc>
          Create a sample IFC4 office model to try the auditor with.

      MepCatalog.Auditor audit <model.ifc> [--api <url>] [--report <report.xlsx|.csv>] [--fix <out.ifc>]
          Check every air terminal, fan, damper and light fixture against the catalog.
          --api     Catalog API address (default http://localhost:5236)
          --report  Write the results to an Excel (.xlsx) or CSV (.csv) file
          --fix     Fill missing/outdated values from the catalog and save the model to a new file
    """;

if (args.Length < 2)
{
    Console.WriteLine(Usage);
    return 1;
}

try
{
    return args[0] switch
    {
        "sample" => CreateSample(args[1]),
        "audit" => await Audit(args[1], Option("--api") ?? "http://localhost:5236", Option("--report"), Option("--fix")),
        _ => ShowUsage(),
    };
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"Could not reach the catalog API: {ex.Message}");
    Console.Error.WriteLine("Is it running? Start it with: dotnet run --project src/MepCatalog.Api");
    return 1;
}

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

int ShowUsage()
{
    Console.WriteLine(Usage);
    return 1;
}

int CreateSample(string path)
{
    SampleBuildingFactory.Create(path);
    Console.WriteLine($"Created sample model: {Path.GetFullPath(path)}");
    return 0;
}

async Task<int> Audit(string modelPath, string apiUrl, string? reportPath, string? fixPath)
{
    if (!File.Exists(modelPath))
    {
        Console.Error.WriteLine($"File not found: {modelPath}");
        return 1;
    }

    using var model = IfcStore.Open(modelPath, SampleBuildingFactory.Credentials);
    var devices = IfcDeviceAdapter.ReadDevices(model);

    using var http = new HttpClient { BaseAddress = new Uri(apiUrl.TrimEnd('/') + "/") };
    var auditor = new DeviceAuditor(new HttpProductCatalog(http));
    var results = await auditor.AuditAsync(devices);

    PrintResults(Path.GetFileName(modelPath), results);

    if (reportPath is not null)
    {
        if (Path.GetExtension(reportPath).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            CsvAuditReport.Write(reportPath, results);
        else
            File.WriteAllBytes(reportPath, ExcelAuditReport.Create(
                new AuditReportInfo(Path.GetFileName(modelPath), DateTime.UtcNow, apiUrl), results));
        Console.WriteLine($"Report written to {Path.GetFullPath(reportPath)}");
    }

    if (fixPath is not null)
    {
        int changed;
        using (var txn = model.BeginTransaction("Apply catalog values"))
        {
            changed = IfcDeviceAdapter.ApplyCatalogValues(model, results);
            txn.Commit();
        }
        model.SaveAs(fixPath);
        Console.WriteLine($"Updated {changed} device(s) from the catalog and saved {Path.GetFullPath(fixPath)}");
    }

    // Non-zero exit code when the model still has problems, so the auditor can gate a CI pipeline.
    return results.All(r => r.Status == AuditStatus.Ok) ? 0 : 2;
}

static void PrintResults(string modelName, IReadOnlyList<DeviceAuditResult> results)
{
    Console.WriteLine($"Audited {results.Count} devices in {modelName}");
    Console.WriteLine();

    foreach (var group in results.GroupBy(r => r.Status).OrderBy(g => g.Key))
    {
        Console.ForegroundColor = group.Key switch
        {
            AuditStatus.Ok => ConsoleColor.Green,
            AuditStatus.NeedsUpdate => ConsoleColor.Yellow,
            _ => ConsoleColor.Red,
        };
        Console.WriteLine($"{AuditStatusText.Label(group.Key)} ({group.Count()})");
        Console.ResetColor();

        foreach (var r in group)
        {
            Console.WriteLine($"  [{r.Device.Level}] {r.Device.Name} - {r.Message}");
            foreach (var c in r.Changes)
                Console.WriteLine($"      {c.Field}: {(c.ModelValue?.ToString() ?? "(missing)")} -> {c.CatalogValue}");
        }
        Console.WriteLine();
    }

    var fixable = results.Count(r => r.CanAutoFix);
    var manual = results.Count(r => r.Status is not (AuditStatus.Ok or AuditStatus.NeedsUpdate));
    Console.WriteLine($"Summary: {results.Count(r => r.Status == AuditStatus.Ok)} ok, {fixable} fixable automatically, {manual} need a designer");
}
