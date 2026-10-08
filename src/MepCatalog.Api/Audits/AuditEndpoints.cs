using MepCatalog.Core.Auditing;
using MepCatalog.Data;
using MepCatalog.Ifc;
using MepCatalog.Reporting;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Xbim.Ifc;

namespace MepCatalog.Api.Audits;

public record AuditSummary(int Total, int Ok, int NeedsUpdate, int Unidentified, int NotInCatalog, int CategoryMismatch);

public record AuditDeviceDto(
    string Id,
    string? Name,
    string ElementType,
    string? Level,
    string? Manufacturer,
    string? Model,
    AuditStatus Status,
    string Message,
    int? ProductId,
    IReadOnlyList<FieldChange> Changes);

/// <param name="ProjectGlobalId">The model's IFC project GlobalId; audits are grouped by it in the history.</param>
/// <param name="Previous">The previous audit of the same model, so the UI can show what changed.</param>
public record AuditResponse(
    string FileName,
    AuditSummary Summary,
    IReadOnlyList<AuditDeviceDto> Devices,
    string? ProjectGlobalId = null,
    string? ProjectName = null,
    AuditRunDto? Previous = null);

public static class AuditEndpoints
{
    private const long MaxModelBytes = 100 * 1024 * 1024;

    public static void MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/audits").WithTags("Audits").DisableAntiforgery()
            .RequireRateLimiting(RateLimits.Uploads);

        group.MapPost("/", Audit)
            .WithSummary("Audit an IFC4 model against the catalog")
            .WithMetadata(new RequestSizeLimitAttribute(MaxModelBytes));
        group.MapPost("/fix", Fix)
            .WithSummary("Fill missing/outdated values from the catalog and return the fixed IFC file")
            .WithMetadata(new RequestSizeLimitAttribute(MaxModelBytes));
        group.MapPost("/report", Report)
            .WithSummary("Audit an IFC4 model and return the results as an Excel workbook")
            .WithMetadata(new RequestSizeLimitAttribute(MaxModelBytes));
    }

    private static async Task<Results<FileContentHttpResult, BadRequest<string>>> Report(
        IFormFile file, DbProductCatalog catalog, HttpRequest request, CancellationToken ct)
    {
        if (Validate(file) is { } error)
            return TypedResults.BadRequest(error);

        using var upload = await TempFile.SaveAsync(file, ct);
        using var model = OpenModel(upload.Path, out var openError);
        if (model is null)
            return TypedResults.BadRequest(openError!);

        var results = await new DeviceAuditor(catalog).AuditAsync(IfcDeviceAdapter.ReadDevices(model), ct);
        var info = new AuditReportInfo(file.FileName, DateTime.UtcNow, $"MepCatalog API ({request.Host})");
        var name = $"{Path.GetFileNameWithoutExtension(file.FileName)}-audit.xlsx";
        return TypedResults.File(ExcelAuditReport.Create(info, results), ExcelAuditReport.ContentType, name);
    }

    private static async Task<Results<Ok<AuditResponse>, BadRequest<string>>> Audit(
        IFormFile file, DbProductCatalog catalog, CatalogDbContext db, CancellationToken ct)
    {
        if (Validate(file) is { } error)
            return TypedResults.BadRequest(error);

        using var upload = await TempFile.SaveAsync(file, ct);
        using var model = OpenModel(upload.Path, out var openError);
        if (model is null)
            return TypedResults.BadRequest(openError!);

        var results = await new DeviceAuditor(catalog).AuditAsync(IfcDeviceAdapter.ReadDevices(model), ct);
        var summary = Summarize(results);
        var response = new AuditResponse(file.FileName, summary, results.Select(ToDto).ToList());

        // Models without an IfcProject can still be audited; they just aren't added to the history.
        if (IfcDeviceAdapter.ReadProject(model) is not { } project)
            return TypedResults.Ok(response);

        var previous = await AuditHistory.RecordAsync(db, new AuditRun
        {
            ProjectGlobalId = project.GlobalId,
            ProjectName = project.Name,
            FileName = file.FileName,
            AuditedUtc = DateTime.UtcNow,
            Total = summary.Total,
            Ok = summary.Ok,
            NeedsUpdate = summary.NeedsUpdate,
            Unidentified = summary.Unidentified,
            NotInCatalog = summary.NotInCatalog,
            CategoryMismatch = summary.CategoryMismatch,
        }, ct);

        return TypedResults.Ok(response with { ProjectGlobalId = project.GlobalId, ProjectName = project.Name, Previous = previous });
    }

    private static async Task<Results<FileContentHttpResult, BadRequest<string>>> Fix(
        IFormFile file, DbProductCatalog catalog, CancellationToken ct)
    {
        if (Validate(file) is { } error)
            return TypedResults.BadRequest(error);

        using var upload = await TempFile.SaveAsync(file, ct);
        using var output = TempFile.Create();
        using (var model = OpenModel(upload.Path, out var openError))
        {
            if (model is null)
                return TypedResults.BadRequest(openError!);
            if (model.SchemaVersion != Xbim.Common.Step21.XbimSchemaVersion.Ifc4)
                return TypedResults.BadRequest($"Only IFC4 models can be fixed. This model is {model.SchemaVersion}.");

            var results = await new DeviceAuditor(catalog).AuditAsync(IfcDeviceAdapter.ReadDevices(model), ct);
            using (var txn = model.BeginTransaction("Apply catalog values"))
            {
                IfcDeviceAdapter.ApplyCatalogValues(model, results);
                txn.Commit();
            }
            model.SaveAs(output.Path);
        }

        var bytes = await File.ReadAllBytesAsync(output.Path, ct);
        var name = $"{Path.GetFileNameWithoutExtension(file.FileName)}-fixed.ifc";
        return TypedResults.File(bytes, "application/x-step", name);
    }

    private static string? Validate(IFormFile file)
    {
        if (file.Length == 0)
            return "The file is empty.";
        if (!string.Equals(Path.GetExtension(file.FileName), ".ifc", StringComparison.OrdinalIgnoreCase))
            return "Please upload an .ifc file.";
        return null;
    }

    private static IfcStore? OpenModel(string path, out string? error)
    {
        try
        {
            error = null;
            return IfcStore.Open(path, SampleBuildingFactory.Credentials);
        }
        catch (Exception ex)
        {
            error = $"Could not read the IFC file: {ex.Message}";
            return null;
        }
    }

    private static AuditSummary Summarize(IReadOnlyList<DeviceAuditResult> results) => new(
        results.Count,
        results.Count(r => r.Status == AuditStatus.Ok),
        results.Count(r => r.Status == AuditStatus.NeedsUpdate),
        results.Count(r => r.Status == AuditStatus.Unidentified),
        results.Count(r => r.Status == AuditStatus.NotInCatalog),
        results.Count(r => r.Status == AuditStatus.CategoryMismatch));

    private static AuditDeviceDto ToDto(DeviceAuditResult r) => new(
        r.Device.Id, r.Device.Name, r.Device.ElementType, r.Device.Level,
        r.Device.Manufacturer, r.Device.Model, r.Status, r.Message, r.Product?.Id, r.Changes);

    /// <summary>xBIM reads and writes files by path, so uploads go through a temp file that is always cleaned up.</summary>
    private sealed class TempFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mepcatalog-{Guid.NewGuid():N}.ifc");

        public static TempFile Create() => new();

        public static async Task<TempFile> SaveAsync(IFormFile file, CancellationToken ct)
        {
            var temp = new TempFile();
            await using var stream = File.Create(temp.Path);
            await file.CopyToAsync(stream, ct);
            return temp;
        }

        public void Dispose()
        {
            try { File.Delete(Path); } catch (IOException) { /* best effort */ }
        }
    }
}
