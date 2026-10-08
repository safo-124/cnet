using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MepCatalog.Api.Datasheets;
using MepCatalog.Api.Products;
using Microsoft.AspNetCore.Hosting;

namespace MepCatalog.Tests;

/// <summary>The settings used by the public deployment: Demo:Enabled with the repository's sample files.</summary>
public class DemoApiFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Demo:Enabled", "true");
        builder.UseSetting("Samples:Path", Path.Combine(FindRepoRoot(), "data"));
        // Even with a key configured, the demo must never call the paid AI service.
        builder.UseSetting("Anthropic:ApiKey", "sk-ant-not-a-real-key");
    }

    /// <summary>Walks up from the test binaries, or from this source file when the build output lives elsewhere.</summary>
    private static string FindRepoRoot([CallerFilePath] string sourceFile = "")
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(sourceFile) })
        {
            var dir = string.IsNullOrEmpty(start) ? null : new DirectoryInfo(start);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MepCatalog.sln")))
                dir = dir.Parent;
            if (dir is not null)
                return dir.FullName;
        }
        throw new InvalidOperationException("Repository root not found.");
    }
}

public class DemoModeTests(DemoApiFactory factory) : IClassFixture<DemoApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Empty_catalog_is_seeded_with_the_sample_products()
    {
        var stats = await _client.GetFromJsonAsync<ProductStats>("/api/products/stats", Json);

        Assert.Equal(14, stats!.Total);
    }

    [Fact]
    public async Task Ai_is_switched_off_with_a_reason_even_when_a_key_is_set()
    {
        var status = await _client.GetFromJsonAsync<DatasheetStatus>("/api/datasheets/status", Json);

        Assert.False(status!.Enabled);
        Assert.Contains("public demo", status.DisabledReason);
    }

    [Theory]
    [InlineData("/samples/sample-building.ifc")]
    [InlineData("/samples/sample-products.csv")]
    [InlineData("/samples/datasheets/nordic-air-ka-series.pdf")]
    public async Task Sample_files_can_be_downloaded(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_check_and_unknown_api_routes_behave()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/healthz")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/does-not-exist")).StatusCode);
    }
}
