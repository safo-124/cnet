using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MepCatalog.Api.Products;
using MepCatalog.Core;
using MepCatalog.Data;

namespace MepCatalog.Tests;

public class ProductApiTests : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client;

    public ProductApiTests(ApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Create_then_get_and_lookup_returns_the_product()
    {
        var input = new ProductInput("Test Oy", "tx-100", ProductCategory.Fan, "Test fan", 100, 50, 160, 4);

        var created = await _client.PostAsJsonAsync("/api/products", input, Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var product = await created.Content.ReadFromJsonAsync<ProductDto>(Json);
        Assert.Equal("TX-100", product!.Model);

        var byId = await _client.GetFromJsonAsync<ProductDto>($"/api/products/{product.Id}", Json);
        Assert.Equal("Test fan", byId!.Description);

        var lookup = await _client.GetAsync("/api/products/lookup?manufacturer=test%20oy&model=TX-100");
        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
    }

    [Fact]
    public async Task Duplicate_manufacturer_and_model_is_rejected()
    {
        var input = new ProductInput("Dup Oy", "D-1", ProductCategory.Damper, null, null, null, 200, null);
        await _client.PostAsJsonAsync("/api/products", input, Json);

        var second = await _client.PostAsJsonAsync("/api/products", input with { Manufacturer = "DUP OY" }, Json);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Invalid_input_returns_validation_problem()
    {
        var input = new ProductInput("", "", ProductCategory.Unknown, null, -5, null, null, null);

        var response = await _client.PostAsJsonAsync("/api/products", input, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("AirflowLps", body);
    }

    [Fact]
    public async Task Csv_import_creates_products_and_reports_bad_rows()
    {
        const string csv = """
            Manufacturer;Model;Category;Airflow;Connection Size
            Import Oy;IM-1;Tuloilmalaite;180 m3/h;Ø160
            Import Oy;IM-2;Puhallin;0,2 m3/s;Ø250
            Import Oy;IM-3;Not a category;10 l/s;Ø100
            """;
        using var content = new MultipartFormDataContent
        {
            { new StringContent(csv, Encoding.UTF8, "text/csv"), "file", "products.csv" },
        };

        var response = await _client.PostAsync("/api/products/import", content);
        response.EnsureSuccessStatusCode();
        var report = await response.Content.ReadFromJsonAsync<ImportReport>(Json);

        Assert.Equal(2, report!.Created);
        Assert.Single(report.Failed);
        Assert.Equal(4, report.Failed[0].RowNumber);

        var search = await _client.GetFromJsonAsync<PagedResult<ProductDto>>(
            "/api/products?search=import&category=Fan", Json);
        var fan = Assert.Single(search!.Items);
        Assert.Equal(200, fan.AirflowLps);
    }

    [Fact]
    public async Task Stats_count_products_per_category_including_empty_ones()
    {
        var input = new ProductInput("Stats Oy", "S-1", ProductCategory.Damper, null, null, null, 160, null);
        await _client.PostAsJsonAsync("/api/products", input, Json);

        var stats = await _client.GetFromJsonAsync<ProductStats>("/api/products/stats", Json);

        Assert.Equal(5, stats!.ByCategory.Count);
        Assert.True(stats.ByCategory[ProductCategory.Damper] >= 1);
        Assert.Equal(stats.Total, stats.ByCategory.Values.Sum());
    }

    [Fact]
    public async Task Delete_removes_the_product()
    {
        var input = new ProductInput("Del Oy", "X-1", ProductCategory.LightFixture, null, null, 20, null, null);
        var created = await (await _client.PostAsJsonAsync("/api/products", input, Json))
            .Content.ReadFromJsonAsync<ProductDto>(Json);

        var delete = await _client.DeleteAsync($"/api/products/{created!.Id}");
        var get = await _client.GetAsync($"/api/products/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }
}
