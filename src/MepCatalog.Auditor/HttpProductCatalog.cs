using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MepCatalog.Core;
using MepCatalog.Core.Auditing;

namespace MepCatalog.Auditor;

/// <summary>Looks up products through the MepCatalog REST API, caching each answer for the run.</summary>
public class HttpProductCatalog(HttpClient http) : IProductCatalog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Dictionary<(string, string), Product?> _cache = new();

    public async Task<Product?> FindAsync(string manufacturer, string model, CancellationToken ct = default)
    {
        var key = (manufacturer.Trim().ToUpperInvariant(), model.Trim().ToUpperInvariant());
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var url = $"api/products/lookup?manufacturer={Uri.EscapeDataString(manufacturer)}&model={Uri.EscapeDataString(model)}";
        using var response = await http.GetAsync(url, ct);
        Product? product = null;
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
            product = await response.Content.ReadFromJsonAsync<Product>(Json, ct);
        }

        _cache[key] = product;
        return product;
    }
}
