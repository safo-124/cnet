using Autodesk.Revit.DB;
using MepCatalog.Core;
using MepCatalog.Core.Auditing;

namespace MepCatalog.Revit;

/// <summary>
/// The Revit adapter: turns family instances into <see cref="ModelDevice"/> records for the shared
/// <see cref="DeviceAuditor"/>, and writes catalog values back. The IFC adapter does the same for IFC files.
/// </summary>
internal static class RevitDevices
{
    public static IReadOnlyList<ModelDevice> Read(Document doc)
    {
        var filter = new ElementMulticategoryFilter(CatalogParameters.DeviceCategories.Keys.ToList());
        return new FilteredElementCollector(doc)
            .WherePasses(filter)
            .WhereElementIsNotElementType()
            .OfType<FamilyInstance>()
            .Select(e => ToDevice(doc, e))
            .OrderBy(d => d.Level).ThenBy(d => d.Name)
            .ToList();
    }

    private static ModelDevice ToDevice(Document doc, FamilyInstance element)
    {
        var (label, expected) = CatalogParameters.DeviceCategories[element.Category.BuiltInCategory];
        var type = doc.GetElement(element.GetTypeId());

        return new ModelDevice
        {
            Id = element.UniqueId,
            Name = DisplayName(element),
            ElementType = label,
            ExpectedCategories = expected,
            Level = LevelName(doc, element),
            // Manufacturer and Model are normally type parameters; an instance value wins if someone set one.
            Manufacturer = Text(element, BuiltInParameter.ALL_MODEL_MANUFACTURER) ?? Text(type, BuiltInParameter.ALL_MODEL_MANUFACTURER),
            Model = Text(element, BuiltInParameter.ALL_MODEL_MODEL) ?? Text(type, BuiltInParameter.ALL_MODEL_MODEL),
            AirflowLps = Number(element, nameof(Product.AirflowLps)),
            PowerW = Number(element, nameof(Product.PowerW)),
            ConnectionSizeMm = Number(element, nameof(Product.ConnectionSizeMm)) is { } mm ? (int)Math.Round(mm) : null,
            WeightKg = Number(element, nameof(Product.WeightKg)),
        };
    }

    /// <summary>
    /// Writes catalog values into the MC_ parameters of every device that can be fixed automatically,
    /// in one transaction so a single Undo reverts it. Returns how many devices changed.
    /// </summary>
    public static int ApplyCatalogValues(Document doc, IEnumerable<DeviceAuditResult> results)
    {
        using var tx = new Transaction(doc, "MepCatalog: fill device data from catalog");
        tx.Start();

        var changed = 0;
        foreach (var result in results.Where(r => r.CanAutoFix))
        {
            if (doc.GetElement(result.Device.Id) is not { } element)
                continue;

            foreach (var change in result.Changes)
                element.LookupParameter(CatalogParameters.NameFor(change.Field))?.Set(change.CatalogValue);
            element.LookupParameter(CatalogParameters.CatalogProductId)?.Set(result.Product!.Id);
            changed++;
        }

        tx.Commit();
        return changed;
    }

    public static ICollection<ElementId> ElementIds(Document doc, IEnumerable<DeviceAuditResult> results) =>
        results.Select(r => doc.GetElement(r.Device.Id)?.Id).OfType<ElementId>().ToList();

    private static string DisplayName(FamilyInstance element)
    {
        var mark = Text(element, BuiltInParameter.ALL_MODEL_MARK);
        var familyAndType = $"{element.Symbol.FamilyName}: {element.Name}";
        return mark is null ? familyAndType : $"{mark} ({familyAndType})";
    }

    private static string? LevelName(Document doc, Element element)
    {
        if (doc.GetElement(element.LevelId) is Level level)
            return level.Name;
        // Hosted families (e.g. ceiling diffusers) often have no LevelId; use their reference level instead.
        return (element.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)
                ?? element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM))?.AsValueString();
    }

    private static string? Text(Element? element, BuiltInParameter parameter) =>
        element?.get_Parameter(parameter)?.AsString() is { } text && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    private static double? Number(Element element, string field) =>
        element.LookupParameter(CatalogParameters.NameFor(field)) is { HasValue: true, StorageType: StorageType.Double } p
            ? p.AsDouble()
            : null;
}
