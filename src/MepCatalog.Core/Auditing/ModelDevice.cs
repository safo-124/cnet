namespace MepCatalog.Core.Auditing;

/// <summary>
/// A device as found in a building model (IFC, Revit, ...), independent of the file format.
/// Adapters for each format turn their elements into this record so the audit logic is shared.
/// </summary>
public record ModelDevice
{
    /// <summary>Stable element id in the source model, e.g. the IFC GlobalId or a Revit UniqueId.</summary>
    public required string Id { get; init; }

    public string? Name { get; init; }

    /// <summary>Element type in the source model, e.g. "IfcAirTerminal".</summary>
    public required string ElementType { get; init; }

    /// <summary>Catalog categories that make sense for this element type.</summary>
    public required IReadOnlyList<ProductCategory> ExpectedCategories { get; init; }

    /// <summary>Building storey / level the element is placed on.</summary>
    public string? Level { get; init; }

    public string? Manufacturer { get; init; }

    public string? Model { get; init; }

    public double? AirflowLps { get; init; }

    public double? PowerW { get; init; }

    public int? ConnectionSizeMm { get; init; }

    public double? WeightKg { get; init; }
}
