namespace MepCatalog.Core;

public enum ProductCategory
{
    Unknown = 0,
    SupplyAirTerminal,
    ExhaustAirTerminal,
    Fan,
    Damper,
    LightFixture,
}

public static class ProductCategoryParser
{
    // Manufacturer files use many names for the same thing, in English and Finnish.
    private static readonly Dictionary<string, ProductCategory> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["supply air terminal"] = ProductCategory.SupplyAirTerminal,
        ["supply diffuser"] = ProductCategory.SupplyAirTerminal,
        ["supply air device"] = ProductCategory.SupplyAirTerminal,
        ["tuloilmalaite"] = ProductCategory.SupplyAirTerminal,
        ["exhaust air terminal"] = ProductCategory.ExhaustAirTerminal,
        ["exhaust valve"] = ProductCategory.ExhaustAirTerminal,
        ["extract air device"] = ProductCategory.ExhaustAirTerminal,
        ["poistoilmalaite"] = ProductCategory.ExhaustAirTerminal,
        ["fan"] = ProductCategory.Fan,
        ["duct fan"] = ProductCategory.Fan,
        ["puhallin"] = ProductCategory.Fan,
        ["damper"] = ProductCategory.Damper,
        ["fire damper"] = ProductCategory.Damper,
        ["palopelti"] = ProductCategory.Damper,
        ["säätöpelti"] = ProductCategory.Damper,
        ["light fixture"] = ProductCategory.LightFixture,
        ["luminaire"] = ProductCategory.LightFixture,
        ["valaisin"] = ProductCategory.LightFixture,
    };

    public static ProductCategory Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ProductCategory.Unknown;

        var key = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (Aliases.TryGetValue(key, out var category))
            return category;

        return Enum.TryParse<ProductCategory>(key.Replace(" ", ""), ignoreCase: true, out var parsed)
            ? parsed
            : ProductCategory.Unknown;
    }
}
