namespace MepCatalog.Core.Auditing;

/// <summary>Looks up catalog products. Implemented over HTTP (REST API) or directly over the database.</summary>
public interface IProductCatalog
{
    Task<Product?> FindAsync(string manufacturer, string model, CancellationToken ct = default);
}

public enum AuditStatus
{
    /// <summary>Device data matches the catalog.</summary>
    Ok,

    /// <summary>Product found; some values are missing or differ and can be filled from the catalog.</summary>
    NeedsUpdate,

    /// <summary>Device has no manufacturer or model, so a designer must choose the product.</summary>
    Unidentified,

    /// <summary>Manufacturer + model is not in the catalog.</summary>
    NotInCatalog,

    /// <summary>The catalog product is a different kind of device, e.g. a fan model on an air terminal.</summary>
    CategoryMismatch,
}

/// <param name="Field">Name of the field, e.g. "AirflowLps".</param>
/// <param name="ModelValue">Value currently in the model (null if missing).</param>
/// <param name="CatalogValue">Value the catalog says it should be.</param>
public record FieldChange(string Field, double? ModelValue, double CatalogValue);

public record DeviceAuditResult(
    ModelDevice Device,
    AuditStatus Status,
    string Message,
    Product? Product,
    IReadOnlyList<FieldChange> Changes)
{
    public bool CanAutoFix => Status == AuditStatus.NeedsUpdate;
}

public class DeviceAuditor(IProductCatalog catalog)
{
    // Values closer than this are treated as equal (e.g. 49.999 l/s vs 50 l/s after unit conversion).
    private const double Tolerance = 0.01;

    public async Task<IReadOnlyList<DeviceAuditResult>> AuditAsync(IEnumerable<ModelDevice> devices, CancellationToken ct = default)
    {
        var results = new List<DeviceAuditResult>();
        foreach (var device in devices)
            results.Add(await AuditAsync(device, ct));
        return results;
    }

    public async Task<DeviceAuditResult> AuditAsync(ModelDevice device, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(device.Manufacturer) || string.IsNullOrWhiteSpace(device.Model))
            return Result(device, AuditStatus.Unidentified, "Manufacturer or model is missing in the model.");

        var product = await catalog.FindAsync(device.Manufacturer, device.Model, ct);
        if (product is null)
            return Result(device, AuditStatus.NotInCatalog, $"{device.Manufacturer} {device.Model} was not found in the catalog.");

        if (!device.ExpectedCategories.Contains(product.Category))
            return Result(device, AuditStatus.CategoryMismatch,
                $"{device.ElementType} is linked to a {product.Category} product.", product);

        var changes = new List<FieldChange>();
        Compare(nameof(Product.AirflowLps), device.AirflowLps, product.AirflowLps, changes);
        Compare(nameof(Product.PowerW), device.PowerW, product.PowerW, changes);
        Compare(nameof(Product.ConnectionSizeMm), device.ConnectionSizeMm, product.ConnectionSizeMm, changes);
        Compare(nameof(Product.WeightKg), device.WeightKg, product.WeightKg, changes);

        if (changes.Count == 0)
            return Result(device, AuditStatus.Ok, "Matches the catalog.", product);

        var missing = changes.Count(c => c.ModelValue is null);
        var differ = changes.Count - missing;
        var message = (missing, differ) switch
        {
            (> 0, 0) => $"{missing} value(s) missing.",
            (0, > 0) => $"{differ} value(s) differ from the catalog.",
            _ => $"{missing} value(s) missing, {differ} differ from the catalog.",
        };
        return new DeviceAuditResult(device, AuditStatus.NeedsUpdate, message, product, changes);
    }

    private static void Compare(string field, double? modelValue, double? catalogValue, List<FieldChange> changes)
    {
        // The catalog has nothing to offer for this field.
        if (catalogValue is null)
            return;

        if (modelValue is null || Math.Abs(modelValue.Value - catalogValue.Value) > Tolerance)
            changes.Add(new FieldChange(field, modelValue, catalogValue.Value));
    }

    private static DeviceAuditResult Result(ModelDevice device, AuditStatus status, string message, Product? product = null)
        => new(device, status, message, product, []);
}
