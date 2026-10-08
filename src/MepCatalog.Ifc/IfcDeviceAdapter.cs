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
/// <para>Manufacturer and model come from the standard <c>Pset_ManufacturerTypeInformation</c>, on the element or
/// its type (element values win).</para>
/// <para>Technical values use the standard IFC4 property sets where IFC defines one, in the project's units
/// (see <see cref="StandardLocations"/>). IFC4 has no standard property for a round connection diameter or for the
/// weight of these devices, so those live in the <c>MepCatalog_ProductData</c> set, with the unit in each property
/// name. Values found only in that set (files written by older versions) are still read.</para>
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

    private enum Measure { FlowRate, Power }

    private sealed record StandardLocation(string Pset, string Property, Measure Measure);

    /// <summary>
    /// Where IFC4 (ADD2 TC1) stores each catalog value, per element type. Names checked against the buildingSMART
    /// documentation. The *TypeCommon sets are "type driven override" sets, so they are valid on an occurrence too.
    /// </summary>
    private static readonly Dictionary<(string ElementType, string Field), StandardLocation> StandardLocations = new()
    {
        [("IfcAirTerminal", nameof(Product.AirflowLps))] = new("Pset_AirTerminalOccurrence", "AirFlowRate", Measure.FlowRate),
        [("IfcFan", nameof(Product.AirflowLps))] = new("Pset_FanTypeCommon", "NominalAirFlowRate", Measure.FlowRate),
        [("IfcFan", nameof(Product.PowerW))] = new("Pset_FanTypeCommon", "NominalPowerRate", Measure.Power),
        [("IfcLightFixture", nameof(Product.PowerW))] = new("Pset_LightFixtureTypeCommon", "TotalWattage", Measure.Power),
    };

    /// <summary>
    /// The model's IfcProject GlobalId and name. The GlobalId survives edits and re-exports, so it identifies
    /// "the same model" across audits even when the file name changes. Null when the file has no project.
    /// </summary>
    public static (string GlobalId, string? Name)? ReadProject(IModel model) =>
        model.Instances.FirstOrDefault<IIfcProject>() is { } project
            ? (project.GlobalId.ToString(), project.Name?.ToString())
            : null;

    public static IReadOnlyList<ModelDevice> ReadDevices(IModel model)
    {
        var units = IfcUnits.Of(model);
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
                AirflowLps = ReadField(element, nameof(Product.AirflowLps), units),
                PowerW = ReadField(element, nameof(Product.PowerW), units),
                ConnectionSizeMm = ReadField(element, nameof(Product.ConnectionSizeMm), units) is { } mm ? (int)Math.Round(mm) : null,
                WeightKg = ReadField(element, nameof(Product.WeightKg), units),
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

        var units = IfcUnits.Of(model);
        var elements = FindElements(model).ToDictionary(e => e.GlobalId.ToString());
        var changed = 0;

        foreach (var result in results.Where(r => r.CanAutoFix))
        {
            if (!elements.TryGetValue(result.Device.Id, out var element))
                continue;

            var obj = (IfcObject)element;
            var data = GetOrCreateOwnPropertySet(model, obj, DataPset);
            foreach (var change in result.Changes)
            {
                if (StandardLocations.TryGetValue((element.ExpressType.ExpressName, change.Field), out var standard))
                {
                    var pset = GetOrCreateOwnPropertySet(model, obj, standard.Pset);
                    SetProperty(model, pset, standard.Property, ToMeasure(change.CatalogValue, standard.Measure, units));
                    // The standard property is now the one source; drop any older copy so the two can't disagree.
                    RemoveProperty(data, change.Field);
                }
                else
                {
                    IfcValue value = change.Field == nameof(Product.ConnectionSizeMm)
                        ? new IfcInteger((long)Math.Round(change.CatalogValue))
                        : new IfcReal(change.CatalogValue);
                    SetProperty(model, data, change.Field, value);
                }
            }
            SetProperty(model, data, "CatalogProductId", new IfcInteger(result.Product!.Id));
            SetProperty(model, data, "CatalogSyncedUtc", new IfcLabel(DateTime.UtcNow.ToString("u")));
            changed++;
        }

        return changed;
    }

    private static IEnumerable<IIfcElement> FindElements(IModel model) =>
        model.Instances.OfType<IIfcElement>().Where(e => ExpectedCategories.ContainsKey(e.ExpressType.ExpressName));

    /// <summary>The standard property in catalog units if the element has it, otherwise the MepCatalog_ProductData value.</summary>
    private static double? ReadField(IIfcElement element, string field, IfcUnits units)
    {
        if (StandardLocations.TryGetValue((element.ExpressType.ExpressName, field), out var standard)
            && FindProperty(element, standard.Pset, standard.Property) is { NominalValue.Value: { } raw } property)
        {
            // A property may carry its own unit, which then wins over the project unit.
            var factor = property.Unit is { } own
                ? IfcUnits.ToSi(own)
                : standard.Measure == Measure.FlowRate ? units.FlowRateFactor : units.PowerFactor;
            var si = Convert.ToDouble(raw) * factor;
            return Math.Round(standard.Measure == Measure.FlowRate ? si * 1000 : si, 6); // m³/s -> l/s; W stays W
        }

        return FindProperty(element, DataPset, field)?.NominalValue?.Value is { } v ? Convert.ToDouble(v) : null;
    }

    /// <summary>Catalog value (l/s or W) as an IFC measure in the project's unit.</summary>
    private static IfcValue ToMeasure(double catalogValue, Measure measure, IfcUnits units) => measure switch
    {
        Measure.FlowRate => new IfcVolumetricFlowRateMeasure(catalogValue / 1000 / units.FlowRateFactor),
        Measure.Power => new IfcPowerMeasure(catalogValue / units.PowerFactor),
        _ => throw new ArgumentOutOfRangeException(nameof(measure)),
    };

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

    private static IIfcPropertySingleValue? FindProperty(IIfcObject obj, string psetName, string property) =>
        PropertySets(obj, psetName)
            .SelectMany(p => p.HasProperties.OfType<IIfcPropertySingleValue>())
            .FirstOrDefault(p => p.Name == property && p.NominalValue is not null);

    private static string? ReadText(IIfcObject obj, string psetName, string property) =>
        FindProperty(obj, psetName, property)?.NominalValue?.Value?.ToString() is { } text && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;

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

    private static void RemoveProperty(IfcPropertySet pset, string name)
    {
        if (pset.HasProperties.OfType<IfcPropertySingleValue>().FirstOrDefault(p => p.Name == name) is { } property)
            pset.HasProperties.Remove(property);
    }
}
