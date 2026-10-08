namespace MepCatalog.Core;

/// <summary>
/// A manufacturer product in the catalog, e.g. a supply air diffuser or a fan.
/// All technical values are stored in SI-based canonical units.
/// </summary>
public class Product
{
    public int Id { get; set; }

    public required string Manufacturer { get; set; }

    public required string Model { get; set; }

    public ProductCategory Category { get; set; }

    public string? Description { get; set; }

    /// <summary>Nominal airflow in litres per second (l/s).</summary>
    public double? AirflowLps { get; set; }

    /// <summary>Electrical power in watts (W).</summary>
    public double? PowerW { get; set; }

    /// <summary>Duct connection diameter in millimetres (mm).</summary>
    public int? ConnectionSizeMm { get; set; }

    /// <summary>Weight in kilograms (kg).</summary>
    public double? WeightKg { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }
}
