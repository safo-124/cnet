using MepCatalog.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MepCatalog.Api.Audits;

public record AuditRunDto(
    int Id,
    string FileName,
    DateTime AuditedUtc,
    int Total,
    int Ok,
    int NeedsUpdate,
    int NeedsDesigner)
{
    public static AuditRunDto From(AuditRun r) => new(
        r.Id, r.FileName, r.AuditedUtc, r.Total, r.Ok, r.NeedsUpdate, r.Unidentified + r.NotInCatalog + r.CategoryMismatch);
}

public record ModelHistorySummary(string ProjectGlobalId, string? ProjectName, int Runs, AuditRunDto First, AuditRunDto Latest);

public record ModelHistory(string ProjectGlobalId, string? ProjectName, IReadOnlyList<AuditRunDto> Runs);

/// <summary>Keeps a summary of every audit, grouped by the model's IFC project GlobalId.</summary>
public static class AuditHistory
{
    // Enough history to show a trend, while keeping a public demo's database small.
    private const int MaxRunsPerModel = 50;

    /// <summary>Saves a run and returns the model's previous run, if there was one.</summary>
    public static async Task<AuditRunDto?> RecordAsync(CatalogDbContext db, AuditRun run, CancellationToken ct)
    {
        var previous = await db.AuditRuns
            .Where(r => r.ProjectGlobalId == run.ProjectGlobalId)
            .OrderByDescending(r => r.AuditedUtc)
            .FirstOrDefaultAsync(ct);

        db.AuditRuns.Add(run);
        await db.SaveChangesAsync(ct);

        await db.AuditRuns
            .Where(r => r.ProjectGlobalId == run.ProjectGlobalId)
            .OrderByDescending(r => r.AuditedUtc)
            .Skip(MaxRunsPerModel)
            .ExecuteDeleteAsync(ct);

        return previous is null ? null : AuditRunDto.From(previous);
    }

    public static void MapAuditHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/audits/history").WithTags("Audits");
        group.MapGet("/", ListModels).WithSummary("Every audited model with its first and latest result");
        group.MapGet("/{projectGlobalId}", GetModel).WithSummary("All audits of one model, oldest first");
    }

    private static async Task<Ok<List<ModelHistorySummary>>> ListModels(CatalogDbContext db, CancellationToken ct)
    {
        // At most 50 runs per model are kept, so grouping in memory stays cheap.
        var runs = await db.AuditRuns.AsNoTracking().OrderBy(r => r.AuditedUtc).ToListAsync(ct);
        var models = runs
            .GroupBy(r => r.ProjectGlobalId)
            .Select(g => new ModelHistorySummary(
                g.Key,
                g.Last().ProjectName,
                g.Count(),
                AuditRunDto.From(g.First()),
                AuditRunDto.From(g.Last())))
            .OrderByDescending(m => m.Latest.AuditedUtc)
            .ToList();
        return TypedResults.Ok(models);
    }

    private static async Task<Results<Ok<ModelHistory>, NotFound>> GetModel(
        string projectGlobalId, CatalogDbContext db, CancellationToken ct)
    {
        var runs = await db.AuditRuns.AsNoTracking()
            .Where(r => r.ProjectGlobalId == projectGlobalId)
            .OrderBy(r => r.AuditedUtc)
            .ToListAsync(ct);
        if (runs.Count == 0)
            return TypedResults.NotFound();

        return TypedResults.Ok(new ModelHistory(projectGlobalId, runs[^1].ProjectName, runs.Select(AuditRunDto.From).ToList()));
    }
}
