using MepCatalog.Core;

namespace MepCatalog.Api.Products;

public record ProductDto(
    int Id,
    string Manufacturer,
    string Model,
    ProductCategory Category,
    string? Description,
    double? AirflowLps,
    double? PowerW,
    int? ConnectionSizeMm,
    double? WeightKg,
    DateTime UpdatedUtc)
{
    public static ProductDto From(Product p) => new(
        p.Id, p.Manufacturer, p.Model, p.Category, p.Description,
        p.AirflowLps, p.PowerW, p.ConnectionSizeMm, p.WeightKg, p.UpdatedUtc);
}

public record ProductInput(
    string Manufacturer,
    string Model,
    ProductCategory Category,
    string? Description,
    double? AirflowLps,
    double? PowerW,
    int? ConnectionSizeMm,
    double? WeightKg)
{
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(Manufacturer)) errors[nameof(Manufacturer)] = ["Manufacturer is required."];
        if (string.IsNullOrWhiteSpace(Model)) errors[nameof(Model)] = ["Model is required."];
        if (Category == ProductCategory.Unknown) errors[nameof(Category)] = ["Category is required."];
        if (AirflowLps < 0) errors[nameof(AirflowLps)] = ["Airflow cannot be negative."];
        if (PowerW < 0) errors[nameof(PowerW)] = ["Power cannot be negative."];
        if (ConnectionSizeMm <= 0) errors[nameof(ConnectionSizeMm)] = ["Connection size must be positive."];
        if (WeightKg < 0) errors[nameof(WeightKg)] = ["Weight cannot be negative."];
        return errors;
    }

    public void ApplyTo(Product product)
    {
        product.Manufacturer = Manufacturer.Trim();
        product.Model = Model.Trim().ToUpperInvariant();
        product.Category = Category;
        product.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        product.AirflowLps = AirflowLps;
        product.PowerW = PowerW;
        product.ConnectionSizeMm = ConnectionSizeMm;
        product.WeightKg = WeightKg;
    }
}

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
