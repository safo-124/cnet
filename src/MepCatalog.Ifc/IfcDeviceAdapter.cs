using MepCatalog.Core;
using MepCatalog.Core.Auditing;
using Xbim.Common;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.Kernel;
using Xbim.Ifc4.MeasureResource;
using Xbim.Ifc4.PropertyResource;

namespace MepCatalog.Ifc;

/// <summary>
/// Reads MEP devices from an IFC4 model into <see cref="ModelDevice"/> records and writes catalog values back.
/// </summary>
/// <remarks>
/// Manufacturer and model come from the standard <c>Pset_ManufacturerTypeInformation</c>, on the element or its type
/// (element values win). Technical values live in the <c>MepCatalog_ProductData</c> property set, with the unit in each
/// property name so the values are unambiguous without relying on the project's unit assignment.
/// </remarks>
public static class IfcDeviceAdapter
{
    public const string ManufacturerPset = "Pset_ManufacturerTypeInformation";
    public const string DataPset = "MepCatalog_ProductData";

    private static readonly Dictionary<string, ProductCategory[]> ExpectedCategories = new()
    {
        ["IfcAirTerminal"] = [ProductCategory.SupplyAirTerminal, ProductCategory.ExhaustAirTerminal],
        ["IfcFan"] = [ProductCategory.Fan],
        ["IfcDamper"] = [ProductCategory.Damper],
        ["IfcLightFixture"] = [ProductCategory.LightFixture],
    };

    public static IReadOnlyList<ModelDevice> ReadDevices(IModel model)
    {
        return FindElements(model)
            .Select(element => new ModelDevice
            {
                Id = element.GlobalId.ToString(),
                Name = element.Name?.ToString(),
                ElementType = element.ExpressType.ExpressName,
                ExpectedCategories = ExpectedCategories[element.ExpressType.ExpressName],
                Level = element.ContainedInStructure.FirstOrDefault()?.RelatingStructure.Name?.ToString(),
                Manufacturer = ReadText(element, ManufacturerPset, "Manufacturer"),
                Model = ReadText(element, ManufacturerPset, "ModelLabel") ?? ReadText(element, ManufacturerPset, "ModelReference"),
                AirflowLps = ReadNumber(element, DataPset, nameof(Product.AirflowLps)),
                PowerW = ReadNumber(element, DataPset, nameof(Product.PowerW)),
                ConnectionSizeMm = ReadNumber(element, DataPset, nameof(Product.ConnectionSizeMm)) is { } mm ? (int)mm : null,
                WeightKg = ReadNumber(element, DataPset, nameof(Product.WeightKg)),
            })
            .OrderBy(d => d.Level).ThenBy(d => d.Name)
            .ToList();
    }

    /// <summary>
    /// Writes catalog values into every element whose audit result can be auto-fixed.
    /// Must be called inside a model transaction. Returns the number of elements changed.
    /// </summary>
    public static int ApplyCatalogValues(IModel model, IEnumerable<DeviceAuditResult> results)
    {
        if (model.SchemaVersion != Xbim.Common.Step21.XbimSchemaVersion.Ifc4)
            throw new NotSupportedException($"Writing is only supported for IFC4 models, not {model.SchemaVersion}.");

        var elements = FindElements(model).ToDictionary(e => e.GlobalId.ToString());
        var changed = 0;

        foreach (var result in results.Where(r => r.CanAutoFix))
        {
            if (!elements.TryGetValue(result.Device.Id, out var element))
                continue;

            var pset = GetOrCreateOwnPropertySet(model, (IfcObject)element, DataPset);
            foreach (var change in result.Changes)
            {
                IfcValue value = change.Field == nameof(Product.ConnectionSizeMm)
                    ? new IfcInteger((long)change.CatalogValue)
                    : new IfcReal(change.CatalogValue);
                SetProperty(model, pset, change.Field, value);
            }
            SetProperty(model, pset, "CatalogProductId", new IfcInteger(result.Product!.Id));
            SetProperty(model, pset, "CatalogSyncedUtc", new IfcLabel(DateTime.UtcNow.ToString("u")));
            changed++;
        }

        return changed;
    }

    private static IEnumerable<IIfcElement> FindElements(IModel model) =>
        model.Instances.OfType<IIfcElement>().Where(e => ExpectedCategories.ContainsKey(e.ExpressType.ExpressName));

    /// <summary>Property sets on the element first, then on its type.</summary>
    private static IEnumerable<IIfcPropertySet> PropertySets(IIfcObject obj, string psetName)
    {
        var own = obj.IsDefinedBy
            .Select(rel => rel.RelatingPropertyDefinition)
            .OfType<IIfcPropertySet>();
        var fromType = obj.IsTypedBy
            .SelectMany(rel => rel.RelatingType.HasPropertySets)
            .OfType<IIfcPropertySet>();
        return own.Concat(fromType).Where(p => p.Name == psetName);
    }

    private static IIfcValue? ReadValue(IIfcObject obj, string psetName, string property) =>
        PropertySets(obj, psetName)
            .SelectMany(p => p.HasProperties.OfType<IIfcPropertySingleValue>())
            .Where(p => p.Name == property && p.NominalValue is not null)
            .Select(p => p.NominalValue)
            .FirstOrDefault();

    private static string? ReadText(IIfcObject obj, string psetName, string property) =>
        ReadValue(obj, psetName, property)?.Value?.ToString() is { Length: > 0 } text && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;

    private static double? ReadNumber(IIfcObject obj, string psetName, string property) =>
        ReadValue(obj, psetName, property)?.Value is { } v ? Convert.ToDouble(v) : null;

    /// <summary>
    /// Returns a property set that belongs only to this element. A set shared with other elements is never
    /// edited in place, so fixing one device can't silently change another.
    /// </summary>
    private static IfcPropertySet GetOrCreateOwnPropertySet(IModel model, IfcObject element, string name)
    {
        var own = element.IsDefinedBy
            .Where(rel => rel.RelatedObjects.Count == 1)
            .Select(rel => rel.RelatingPropertyDefinition)
            .OfType<IfcPropertySet>()
            .FirstOrDefault(p => p.Name == name);
        if (own is not null)
            return own;

        var pset = model.Instances.New<IfcPropertySet>(p =>
        {
            p.GlobalId = Guid.NewGuid();
            p.Name = name;
        });

        // Keep any values the element already had from a shared set.
        var shared = element.IsDefinedBy
            .Select(rel => rel.RelatingPropertyDefinition)
            .OfType<IfcPropertySet>()
            .FirstOrDefault(p => p.Name == name);
        foreach (var property in shared?.HasProperties.OfType<IfcPropertySingleValue>() ?? [])
            SetProperty(model, pset, property.Name, (IfcValue)property.NominalValue);

        var sharedRel = element.IsDefinedBy.FirstOrDefault(rel => rel.RelatingPropertyDefinition == shared);
        sharedRel?.RelatedObjects.Remove(element);

        model.Instances.New<IfcRelDefinesByProperties>(rel =>
        {
            rel.GlobalId = Guid.NewGuid();
            rel.RelatingPropertyDefinition = pset;
            rel.RelatedObjects.Add(element);
        });
        return pset;
    }

    private static void SetProperty(IModel model, IfcPropertySet pset, string name, IfcValue value)
    {
        var property = pset.HasProperties.OfType<IfcPropertySingleValue>().FirstOrDefault(p => p.Name == name);
        if (property is null)
        {
            property = model.Instances.New<IfcPropertySingleValue>(p => p.Name = name);
            pset.HasProperties.Add(property);
        }
        property.NominalValue = value;
    }
}
