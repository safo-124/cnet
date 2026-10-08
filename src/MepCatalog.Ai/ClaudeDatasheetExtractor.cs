using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using MepCatalog.Core;

namespace MepCatalog.Ai;

/// <summary>
/// Reads a manufacturer PDF datasheet with Claude and returns the products in it.
/// </summary>
/// <remarks>
/// Claude only reads: it returns each value exactly as printed (with its unit), constrained to a JSON schema.
/// Unit conversion and validation then run through the same <see cref="ProductNormalizer"/> as the CSV import,
/// so the numbers are converted by tested code rather than by the model.
/// </remarks>
public sealed class ClaudeDatasheetExtractor(AnthropicClient client) : IDatasheetExtractor
{
    public const string Model = "claude-opus-5-5";

    private const string Instructions = """
        You read HVAC and electrical product datasheets for a building-services product catalog.

        Find every distinct product model in the attached datasheet. A datasheet often covers a series
        (for example sizes 125, 160 and 200 of the same diffuser) - return one entry per model code.

        For each product:
        - manufacturer: the company name as printed.
        - model: the model or product code that identifies this exact size or variant.
        - category: the kind of device. Use Unknown only if none of the listed kinds fits.
        - description: a short plain description, e.g. "Round ceiling supply diffuser".
        - airflow, power, connectionSize, weight: copy the value exactly as printed, including its unit
          and decimal separator (for example "180 m3/h", "0,35 kW", "Ø160", "1,6 kg"). If several values are
          given (a range, or min/nominal/max), use the nominal value. Use null when the datasheet does not say.

        Never guess or calculate a value that is not printed. Put anything a reviewer should double-check
        (ranges you reduced to one value, unclear tables, values you left out) in notes.
        """;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool IsConfigured => true;

    public async Task<DatasheetExtraction> ExtractAsync(byte[] pdf, CancellationToken ct = default)
    {
        var response = await client.Beta.Messages.Create(new MessageCreateParams
        {
            Model = Model,
            MaxTokens = 16000,
            // If a safety classifier declines the request, the server retries it on a suitable fallback model.
            Betas = ["server-side-fallback-2026-07-01"],
            Fallbacks = new Default(),
            OutputConfig = new BetaOutputConfig
            {
                Effort = Effort.Medium,
                Format = new BetaJsonOutputFormat { Schema = Schema },
            },
            System = Instructions,
            Messages =
            [
                new BetaMessageParam
                {
                    Role = Role.User,
                    Content = new List<BetaContentBlockParam>
                    {
                        new BetaRequestDocumentBlock { Source = new BetaBase64PdfSource { Data = Convert.ToBase64String(pdf) } },
                        new BetaTextBlockParam { Text = "Extract the products from this datasheet." },
                    },
                },
            ],
        }, ct);

        if (response.StopReason == "refusal")
            throw new DatasheetExtractionException("The AI service declined to read this document.");
        if (response.StopReason == "max_tokens")
            throw new DatasheetExtractionException("The datasheet has too many products to read in one go. Try a shorter document.");

        var text = string.Concat(response.Content
            .Select(b => b.TryPickText(out var t) ? t.Text : null)
            .Where(t => t is not null));
        var result = JsonSerializer.Deserialize<ExtractionJson>(text, Json)
            ?? throw new DatasheetExtractionException("The AI service returned an empty answer.");

        return ToExtraction(result);
    }

    /// <summary>Runs the model's raw values through the shared normalizer. Public so it can be tested without the API.</summary>
    public static DatasheetExtraction ToExtraction(ExtractionJson result)
    {
        var products = result.Products.Select(p =>
        {
            var raw = new RawProductRow(p.Manufacturer, p.Model, p.Category, p.Description,
                p.Airflow, p.Power, p.ConnectionSize, p.Weight);
            var normalized = ProductNormalizer.Normalize(raw);
            return new ExtractedProduct(raw, normalized.Product, normalized.Errors, normalized.Product ?? PartialDraft(raw));
        }).ToList();

        return new DatasheetExtraction(products, string.IsNullOrWhiteSpace(result.Notes) ? null : result.Notes.Trim());
    }

    /// <summary>Keeps each value that parses on its own, so the reviewer only fills in what is really missing.</summary>
    private static Product PartialDraft(RawProductRow raw)
    {
        static T? OrNull<T>(Func<T?> parse) where T : struct
        {
            try { return parse(); }
            catch (FormatException) { return null; }
        }

        return new Product
        {
            Manufacturer = raw.Manufacturer?.Trim() ?? "",
            Model = raw.Model?.Trim().ToUpperInvariant() ?? "",
            Category = ProductCategoryParser.Parse(raw.Category),
            Description = string.IsNullOrWhiteSpace(raw.Description) ? null : raw.Description.Trim(),
            AirflowLps = OrNull(() => UnitParser.ParseAirflowLps(raw.Airflow)),
            PowerW = OrNull(() => UnitParser.ParsePowerW(raw.Power)),
            ConnectionSizeMm = OrNull(() => UnitParser.ParseConnectionSizeMm(raw.ConnectionSize)),
            WeightKg = OrNull(() => UnitParser.ParseWeightKg(raw.Weight)),
        };
    }

    public record ExtractionJson(
        [property: JsonPropertyName("products")] IReadOnlyList<ProductJson> Products,
        [property: JsonPropertyName("notes")] string? Notes);

    public record ProductJson(
        string? Manufacturer,
        string? Model,
        string? Category,
        string? Description,
        string? Airflow,
        string? Power,
        string? ConnectionSize,
        string? Weight);

    private static Dictionary<string, JsonElement> Schema { get; } = BuildSchema();

    private static Dictionary<string, JsonElement> BuildSchema()
    {
        object NullableString(string description) => new
        {
            anyOf = new object[] { new { type = "string" }, new { type = "null" } },
            description,
        };

        var categories = Enum.GetNames<ProductCategory>().Where(c => c != nameof(ProductCategory.Unknown)).Append("Unknown");
        var product = new
        {
            type = "object",
            additionalProperties = false,
            required = new[] { "manufacturer", "model", "category", "description", "airflow", "power", "connectionSize", "weight" },
            properties = new Dictionary<string, object>
            {
                ["manufacturer"] = NullableString("Manufacturer name as printed"),
                ["model"] = NullableString("Model code of this exact variant"),
                ["category"] = new { type = "string", @enum = categories },
                ["description"] = NullableString("Short plain description"),
                ["airflow"] = NullableString("Nominal airflow exactly as printed, with unit"),
                ["power"] = NullableString("Electrical power exactly as printed, with unit"),
                ["connectionSize"] = NullableString("Duct connection size exactly as printed, with unit or Ø"),
                ["weight"] = NullableString("Weight exactly as printed, with unit"),
            },
        };

        return new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
            ["required"] = JsonSerializer.SerializeToElement(new[] { "products", "notes" }),
            ["properties"] = JsonSerializer.SerializeToElement(new Dictionary<string, object>
            {
                ["products"] = new { type = "array", items = product },
                ["notes"] = NullableString("Anything the reviewer should double-check"),
            }),
        };
    }
}
