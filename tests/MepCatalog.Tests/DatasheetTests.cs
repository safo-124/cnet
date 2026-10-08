using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MepCatalog.Ai;
using MepCatalog.Api.Datasheets;
using MepCatalog.Api.Products;
using MepCatalog.Core;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static MepCatalog.Ai.ClaudeDatasheetExtractor;

namespace MepCatalog.Tests;

public class DatasheetTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly byte[] FakePdf = "%PDF-1.7 fake"u8.ToArray();

    // What the model returns: values exactly as printed in the datasheet.
    private static readonly ExtractionJson ModelAnswer = new(
    [
        new ProductJson("Nordic Air Oy", "KA-250", "SupplyAirTerminal", "Round ceiling diffuser", "432 m3/h", null, "Ø250", "3,1 kg"),
        new ProductJson("Nordic Air Oy", "KA-315", "SupplyAirTerminal", "Round ceiling diffuser", "160 l/s", null, "Ø315", "3,9 kg"),
        new ProductJson("Nordic Air Oy", "KA-400", "Unknown", null, "about a lot", null, "Ø400", "5,6 kg"),
    ], "KA-400 airflow was given as a chart only.");

    [Fact]
    public void Model_values_are_converted_by_the_shared_normalizer()
    {
        var result = ToExtraction(ModelAnswer);

        var ka250 = result.Products[0].Product!;
        Assert.Equal(120, ka250.AirflowLps);        // 432 m3/h -> 120 l/s
        Assert.Equal(250, ka250.ConnectionSizeMm);
        Assert.Equal(3.1, ka250.WeightKg);
        Assert.Equal("KA-400 airflow was given as a chart only.", result.Notes);
    }

    [Fact]
    public void Unreadable_values_become_issues_for_the_reviewer_instead_of_guesses()
    {
        var ka400 = ToExtraction(ModelAnswer).Products[2];

        Assert.Null(ka400.Product);
        Assert.Contains(ka400.Issues, i => i.Contains("Unknown category"));
        Assert.Contains(ka400.Issues, i => i.StartsWith("Airflow"));
        Assert.Equal("about a lot", ka400.Raw.Airflow);

        // The values that could be read are kept, so the reviewer only fills in the gap.
        Assert.Null(ka400.Draft.AirflowLps);
        Assert.Equal(400, ka400.Draft.ConnectionSizeMm);
        Assert.Equal(5.6, ka400.Draft.WeightKg);
        Assert.Equal("KA-400", ka400.Draft.Model);
    }

    [Fact]
    public async Task Status_reports_disabled_without_an_api_key()
    {
        var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<DatasheetStatus>("/api/datasheets/status", Json);
        var extract = await client.PostAsync("/api/datasheets/extract", Upload(FakePdf, "sheet.pdf"));

        Assert.False(status!.Enabled);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, extract.StatusCode);
    }

    [Fact]
    public async Task Extract_returns_products_for_review_and_flags_ones_already_in_the_catalog()
    {
        var client = ClientWith(new FakeExtractor());
        var existing = new ProductInput("Nordic Air Oy", "KA-250", ProductCategory.SupplyAirTerminal, null, 110, null, 250, 3);
        (await client.PostAsJsonAsync("/api/products", existing, Json)).EnsureSuccessStatusCode();

        var response = await client.PostAsync("/api/datasheets/extract", Upload(FakePdf, "ka-series.pdf"));
        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadFromJsonAsync<DatasheetResponse>(Json))!;

        Assert.Equal(3, body.Products.Count);
        Assert.NotNull(body.Products[0].ExistingProductId);
        Assert.Null(body.Products[1].ExistingProductId);
        Assert.Equal(160, body.Products[1].Product!.AirflowLps);
        Assert.Null(body.Products[2].Product);
        Assert.NotEmpty(body.Products[2].Issues);
        Assert.Equal(400, body.Products[2].Draft.ConnectionSizeMm);

        // Extraction only proposes products; nothing new is saved until the user confirms.
        var lookup = await client.GetAsync("/api/products/lookup?manufacturer=Nordic%20Air%20Oy&model=KA-315");
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
    }

    [Fact]
    public async Task Non_pdf_upload_is_rejected_before_calling_the_ai()
    {
        var extractor = new FakeExtractor();
        var client = ClientWith(extractor);

        var response = await client.PostAsync("/api/datasheets/extract", Upload("hello"u8.ToArray(), "notes.pdf"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, extractor.Calls);
    }

    private HttpClient ClientWith(IDatasheetExtractor extractor) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton(extractor))).CreateClient();

    private static MultipartFormDataContent Upload(byte[] bytes, string fileName) => new()
    {
        { new ByteArrayContent(bytes), "file", fileName },
    };

    private sealed class FakeExtractor : IDatasheetExtractor
    {
        public int Calls { get; private set; }

        public bool IsConfigured => true;

        public Task<DatasheetExtraction> ExtractAsync(byte[] pdf, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(ToExtraction(ModelAnswer));
        }
    }
}
