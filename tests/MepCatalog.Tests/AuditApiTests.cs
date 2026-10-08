using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MepCatalog.Api.Audits;
using MepCatalog.Api.Products;
using MepCatalog.Core;
using MepCatalog.Ifc;
using MepCatalog.Reporting;

namespace MepCatalog.Tests;

public class AuditApiTests : IClassFixture<ApiFactory>, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client;
    private readonly string _modelPath = Path.Combine(Path.GetTempPath(), $"audit-api-{Guid.NewGuid():N}.ifc");

    public AuditApiTests(ApiFactory factory)
    {
        _client = factory.CreateClient();
        SampleBuildingFactory.Create(_modelPath);
    }

    [Fact]
    public async Task Audit_then_fix_then_audit_again_leaves_only_designer_cases()
    {
        await SeedCatalog();

        var before = await Audit(await File.ReadAllBytesAsync(_modelPath));
        Assert.Equal(14, before.Summary.Total);
        Assert.Equal(8, before.Summary.NeedsUpdate);

        using var fixContent = Upload(await File.ReadAllBytesAsync(_modelPath), "sample.ifc");
        var fixResponse = await _client.PostAsync("/api/audits/fix", fixContent);
        fixResponse.EnsureSuccessStatusCode();
        Assert.Equal("sample-fixed.ifc", fixResponse.Content.Headers.ContentDisposition?.FileName);

        var after = await Audit(await fixResponse.Content.ReadAsByteArrayAsync());
        Assert.Equal(0, after.Summary.NeedsUpdate);
        Assert.Equal(11, after.Summary.Ok);
    }

    [Fact]
    public async Task Report_returns_an_excel_workbook()
    {
        using var content = Upload(await File.ReadAllBytesAsync(_modelPath), "office.ifc");

        var response = await _client.PostAsync("/api/audits/report", content);

        response.EnsureSuccessStatusCode();
        Assert.Equal(ExcelAuditReport.ContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("office-audit.xlsx", response.Content.Headers.ContentDisposition?.FileName);
        using var workbook = new ClosedXML.Excel.XLWorkbook(await response.Content.ReadAsStreamAsync());
        Assert.Equal(14, workbook.Worksheet("All devices").Table("Devices").DataRange.RowCount());
    }

    [Fact]
    public async Task Non_ifc_file_is_rejected()
    {
        using var content = Upload("hello"u8.ToArray(), "notes.txt");

        var response = await _client.PostAsync("/api/audits", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Corrupt_ifc_file_is_rejected_with_a_message()
    {
        using var content = Upload("this is not STEP data"u8.ToArray(), "broken.ifc");

        var response = await _client.PostAsync("/api/audits", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Could not read the IFC file", await response.Content.ReadAsStringAsync());
    }

    private async Task<AuditResponse> Audit(byte[] model)
    {
        using var content = Upload(model, "model.ifc");
        var response = await _client.PostAsync("/api/audits", content);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuditResponse>(Json))!;
    }

    private static MultipartFormDataContent Upload(byte[] bytes, string fileName) => new()
    {
        { new ByteArrayContent(bytes), "file", fileName },
    };

    /// <summary>The same products as data/sample-products.csv, after its duplicate KA-125 row updated airflow to 36 l/s.</summary>
    private async Task SeedCatalog()
    {
        ProductInput[] products =
        [
            new("Nordic Air Oy", "KA-125", ProductCategory.SupplyAirTerminal, null, 36, null, 125, 1.2),
            new("Nordic Air Oy", "KA-160", ProductCategory.SupplyAirTerminal, null, 50, null, 160, 1.6),
            new("Nordic Air Oy", "KA-200", ProductCategory.SupplyAirTerminal, null, 90, null, 200, 2.1),
            new("Nordic Air Oy", "KP-125", ProductCategory.ExhaustAirTerminal, null, 30, null, 125, 0.6),
            new("Nordic Air Oy", "KP-160", ProductCategory.ExhaustAirTerminal, null, 45, null, 160, 0.8),
            new("VentoTech", "VT-EC 315", ProductCategory.Fan, null, 700, 310, 315, 9.8),
            new("VentoTech", "FD-200", ProductCategory.Damper, null, null, null, 200, 3.4),
            new("Lumo Lighting", "LX-600 Panel", ProductCategory.LightFixture, null, null, 28, null, 3.2),
            new("Lumo Lighting", "LX-Downlight 18", ProductCategory.LightFixture, null, null, 18, null, 0.4),
            new("Lumo Lighting", "LX-Line 1500", ProductCategory.LightFixture, null, null, 42, null, 2.7),
        ];
        foreach (var product in products)
        {
            var response = await _client.PostAsJsonAsync("/api/products", product, Json);
            Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict);
        }
    }

    public void Dispose() => File.Delete(_modelPath);
}
