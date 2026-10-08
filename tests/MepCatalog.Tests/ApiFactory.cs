using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace MepCatalog.Tests;

/// <summary>
/// Runs the real API in memory against a private in-memory SQLite database.
/// The open <see cref="SqliteConnection"/> keeps the shared in-memory database alive for the factory's lifetime.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString = $"Data Source=file:test-{Guid.NewGuid():N}?mode=memory&cache=shared";
    private readonly SqliteConnection _keepAlive;

    public ApiFactory()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Catalog", _connectionString);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _keepAlive.Dispose();
    }
}
