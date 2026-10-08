using MepCatalog.Api.Audits;
using MepCatalog.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MepCatalog.Tests;

public class AuditHistoryTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly CatalogDbContext _db;

    public AuditHistoryTests()
    {
        _connection.Open();
        _db = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>().UseSqlite(_connection).Options);
        _db.Database.Migrate();
    }

    private static AuditRun Run(string project, int minutesAgo, int ok = 1) => new()
    {
        ProjectGlobalId = project,
        FileName = "model.ifc",
        AuditedUtc = DateTime.UtcNow.AddMinutes(-minutesAgo),
        Total = 10,
        Ok = ok,
    };

    [Fact]
    public async Task Record_returns_the_previous_run_of_the_same_model_only()
    {
        Assert.Null(await AuditHistory.RecordAsync(_db, Run("model-a", 30, ok: 2), default));
        await AuditHistory.RecordAsync(_db, Run("model-b", 20, ok: 9), default);

        var previous = await AuditHistory.RecordAsync(_db, Run("model-a", 10, ok: 7), default);

        Assert.Equal(2, previous!.Ok);
    }

    [Fact]
    public async Task Only_the_latest_fifty_runs_per_model_are_kept()
    {
        for (var i = 60; i > 0; i--)
            await AuditHistory.RecordAsync(_db, Run("busy-model", minutesAgo: i), default);
        await AuditHistory.RecordAsync(_db, Run("other-model", 1), default);

        Assert.Equal(50, await _db.AuditRuns.CountAsync(r => r.ProjectGlobalId == "busy-model"));
        Assert.Equal(1, await _db.AuditRuns.CountAsync(r => r.ProjectGlobalId == "other-model"));
        // The oldest runs are the ones removed.
        var oldestKept = await _db.AuditRuns.Where(r => r.ProjectGlobalId == "busy-model").MinAsync(r => r.AuditedUtc);
        Assert.True(oldestKept > DateTime.UtcNow.AddMinutes(-51));
    }

    [Fact]
    public async Task Dates_read_back_from_sqlite_are_marked_as_utc()
    {
        await AuditHistory.RecordAsync(_db, Run("model-a", 5), default);
        _db.ChangeTracker.Clear();

        var run = await _db.AuditRuns.SingleAsync();

        // Without this, JSON would omit the "Z" and browsers would show the time shifted by their UTC offset.
        Assert.Equal(DateTimeKind.Utc, run.AuditedUtc.Kind);
        Assert.EndsWith("Z", System.Text.Json.JsonSerializer.Serialize(run.AuditedUtc).Trim('"'));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
