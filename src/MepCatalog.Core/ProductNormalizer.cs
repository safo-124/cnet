namespace MepCatalog.Core;

/// <summary>One product row as it appears in a manufacturer file, before any cleaning.</summary>
public record RawProductRow(
    string? Manufacturer,
    string? Model,
    string? Category,
    string? Description,
    string? Airflow,
    string? Power,
    string? ConnectionSize,
    string? Weight);

public record NormalizeResult(Product? Product, IReadOnlyList<string> Errors)
{
    public bool Success => Product is not null;
}

/// <summary>Turns a raw manufacturer row into a clean <see cref="Product"/>, collecting every problem it finds.</summary>
public static class ProductNormalizer
{
    public static NormalizeResult Normalize(RawProductRow row)
    {
        var errors = new List<string>();

        var manufacturer = CleanText(row.Manufacturer);
        var model = CleanText(row.Model);
        if (manufacturer is null) errors.Add("Manufacturer is missing.");
        if (model is null) errors.Add("Model is missing.");

        var category = ProductCategoryParser.Parse(row.Category);
        if (category == ProductCategory.Unknown)
            errors.Add($"Unknown category '{row.Category}'.");

        var airflow = TryParse(() => UnitParser.ParseAirflowLps(row.Airflow), "Airflow", errors);
        var power = TryParse(() => UnitParser.ParsePowerW(row.Power), "Power", errors);
        var size = TryParse(() => UnitParser.ParseConnectionSizeMm(row.ConnectionSize), "Connection size", errors);
        var weight = TryParse(() => UnitParser.ParseWeightKg(row.Weight), "Weight", errors);

        if (errors.Count > 0)
            return new NormalizeResult(null, errors);

        return new NormalizeResult(new Product
        {
            Manufacturer = manufacturer!,
            Model = model!.ToUpperInvariant(),
            Category = category,
            Description = CleanText(row.Description),
            AirflowLps = airflow,
            PowerW = power,
            ConnectionSizeMm = size,
            WeightKg = weight,
        }, errors);
    }

    private static string? CleanText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static T? TryParse<T>(Func<T?> parse, string field, List<string> errors) where T : struct
    {
        try
        {
            return parse();
        }
        catch (FormatException ex)
        {
            errors.Add($"{field}: {ex.Message}");
            return null;
        }
    }
}
