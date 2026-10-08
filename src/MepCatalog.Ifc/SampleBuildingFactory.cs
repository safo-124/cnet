using Xbim.Common;
using Xbim.Common.Step21;
using Xbim.Ifc;
using Xbim.Ifc4.ElectricalDomain;
using Xbim.Ifc4.HvacDomain;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.Kernel;
using Xbim.Ifc4.MeasureResource;
using Xbim.Ifc4.ProductExtension;
using Xbim.Ifc4.PropertyResource;
using Xbim.IO;

namespace MepCatalog.Ifc;

/// <summary>
/// Creates a small two-storey office model with HVAC and lighting devices in typical states:
/// complete, missing values, outdated values, unidentified, not in the catalog and wrongly linked.
/// </summary>
public static class SampleBuildingFactory
{
    public static XbimEditorCredentials Credentials { get; } = new()
    {
        ApplicationDevelopersName = "MepCatalog",
        ApplicationFullName = "MepCatalog Auditor",
        ApplicationIdentifier = "MepCatalog",
        ApplicationVersion = "1.0",
        EditorsFamilyName = "Auditor",
        EditorsGivenName = "MepCatalog",
        EditorsOrganisationName = "MepCatalog",
    };

    private record DeviceSpec(
        string Level, string Name, Func<IModel, IfcElement> Create,
        string? Manufacturer, string? Model,
        double? AirflowLps = null, double? PowerW = null, int? ConnectionSizeMm = null, double? WeightKg = null);

    private static readonly DeviceSpec[] Devices =
    [
        // Level 1
        new("Level 1", "AT-101 Office 101 supply", m => m.Instances.New<IfcAirTerminal>(), "Nordic Air Oy", "KA-125", 35, null, 125, 1.2),
        new("Level 1", "AT-102 Office 102 supply", m => m.Instances.New<IfcAirTerminal>(), "Nordic Air Oy", "KA-160"),
        new("Level 1", "AT-103 Meeting room supply", m => m.Instances.New<IfcAirTerminal>(), "Nordic Air Oy", "KA-160", 50, null, 160, 1.6),
        new("Level 1", "AT-104 WC exhaust", m => m.Instances.New<IfcAirTerminal>(), "Nordic Air Oy", "KP-125"),
        new("Level 1", "AT-105 Corridor supply", m => m.Instances.New<IfcAirTerminal>(), null, null, 40),
        new("Level 1", "FAN-101 Kitchen exhaust fan", m => m.Instances.New<IfcFan>(), "VentoTech", "VT-EC 315", 700, 310, 315, 9.8),
        new("Level 1", "DMP-101 Fire damper shaft A", m => m.Instances.New<IfcDamper>(), "VentoTech", "FD-200", null, null, 200),
        new("Level 1", "LT-101 Open office panel", m => m.Instances.New<IfcLightFixture>(), "Lumo Lighting", "LX-600 Panel"),
        // Level 2
        new("Level 2", "AT-201 Office 201 supply", m => m.Instances.New<IfcAirTerminal>(), "Nordic Air Oy", "KA-200"),
        new("Level 2", "AT-202 Server room supply", m => m.Instances.New<IfcAirTerminal>(), "Nordic Air Oy", "KA-315", 150, null, 315),
        new("Level 2", "AT-203 Storage exhaust", m => m.Instances.New<IfcAirTerminal>(), "nordic air oy", "kp-160"),
        new("Level 2", "FAN-201 Toilet exhaust fan", m => m.Instances.New<IfcFan>(), "Nordic Air Oy", "KA-160"),
        new("Level 2", "LT-201 Lobby downlight", m => m.Instances.New<IfcLightFixture>(), "Lumo Lighting", "LX-Downlight 18", null, 18, null, 0.4),
        new("Level 2", "LT-202 Corridor linear light", m => m.Instances.New<IfcLightFixture>(), "Lumo Lighting", "LX-Line 1500", null, 40, null, 2.7),
    ];

    public static void Create(string path)
    {
        using var model = IfcStore.Create(Credentials, XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel);
        using (var txn = model.BeginTransaction("Create sample building"))
        {
            var project = model.Instances.New<IfcProject>(p => p.Name = "Demo Office Building");
            project.UnitsInContext = CreateUnits(model);
            var site = model.Instances.New<IfcSite>(s => s.Name = "Demo Site");
            var building = model.Instances.New<IfcBuilding>(b => b.Name = "Office A");
            Aggregate(model, project, site);
            Aggregate(model, site, building);

            var storeys = Devices.Select(d => d.Level).Distinct().Select((name, i) =>
                model.Instances.New<IfcBuildingStorey>(s =>
                {
                    s.Name = name;
                    s.Elevation = i * 3500;
                })).ToDictionary(s => s.Name!.Value.ToString());
            foreach (var storey in storeys.Values)
                Aggregate(model, building, storey);

            foreach (var group in Devices.GroupBy(d => d.Level))
            {
                var contained = model.Instances.New<IfcRelContainedInSpatialStructure>(r => r.RelatingStructure = storeys[group.Key]);
                foreach (var spec in group)
                {
                    var element = spec.Create(model);
                    element.Name = spec.Name;
                    contained.RelatedElements.Add(element);
                    AddManufacturerInfo(model, element, spec);
                    AddProductData(model, element, spec);
                }
            }

            txn.Commit();
        }

        model.SaveAs(path);
    }

    private static void Aggregate(IModel model, IfcObjectDefinition parent, IfcObjectDefinition child) =>
        model.Instances.New<IfcRelAggregates>(r =>
        {
            r.RelatingObject = parent;
            r.RelatedObjects.Add(child);
        });

    private static void AddManufacturerInfo(IModel model, IfcElement element, DeviceSpec spec)
    {
        var values = new List<(string, IfcValue?)>
        {
            ("Manufacturer", spec.Manufacturer is null ? null : new IfcLabel(spec.Manufacturer)),
            ("ModelLabel", spec.Model is null ? null : new IfcLabel(spec.Model)),
        };
        AddPropertySet(model, element, IfcDeviceAdapter.ManufacturerPset, values);
    }

    /// <summary>
    /// Project units as a design tool would export them: lengths in mm, power in W, and airflow in litres per
    /// second (a derived unit: litre, a conversion-based unit of 0.001 m³, divided by second).
    /// </summary>
    private static IfcUnitAssignment CreateUnits(IModel model)
    {
        IfcSIUnit Si(IfcUnitEnum type, IfcSIUnitName name, IfcSIPrefix? prefix = null) => model.Instances.New<IfcSIUnit>(u =>
        {
            u.UnitType = type;
            u.Name = name;
            u.Prefix = prefix;
        });

        var litre = model.Instances.New<IfcConversionBasedUnit>(u =>
        {
            u.Name = "LITRE";
            u.UnitType = IfcUnitEnum.VOLUMEUNIT;
            u.Dimensions = model.Instances.New<IfcDimensionalExponents>(d => d.LengthExponent = 3);
            u.ConversionFactor = model.Instances.New<IfcMeasureWithUnit>(m =>
            {
                m.ValueComponent = new IfcVolumeMeasure(0.001);
                m.UnitComponent = Si(IfcUnitEnum.VOLUMEUNIT, IfcSIUnitName.CUBIC_METRE);
            });
        });
        var litresPerSecond = model.Instances.New<IfcDerivedUnit>(u =>
        {
            u.UnitType = IfcDerivedUnitEnum.VOLUMETRICFLOWRATEUNIT;
            u.Elements.Add(model.Instances.New<IfcDerivedUnitElement>(e => { e.Unit = litre; e.Exponent = 1; }));
            u.Elements.Add(model.Instances.New<IfcDerivedUnitElement>(e => { e.Unit = Si(IfcUnitEnum.TIMEUNIT, IfcSIUnitName.SECOND); e.Exponent = -1; }));
        });

        return model.Instances.New<IfcUnitAssignment>(a =>
        {
            a.Units.Add(Si(IfcUnitEnum.LENGTHUNIT, IfcSIUnitName.METRE, IfcSIPrefix.MILLI));
            a.Units.Add(Si(IfcUnitEnum.POWERUNIT, IfcSIUnitName.WATT));
            a.Units.Add(litresPerSecond);
        });
    }

    /// <summary>
    /// Airflow and power go where IFC4 defines them, in project units (l/s and W here). Connection size and weight
    /// have no IFC4 standard property for these devices, so they go in the MepCatalog_ProductData set.
    /// </summary>
    private static void AddProductData(IModel model, IfcElement element, DeviceSpec spec)
    {
        IfcValue? Flow(double? lps) => lps is { } v ? new IfcVolumetricFlowRateMeasure(v) : null;
        IfcValue? Power(double? w) => w is { } v ? new IfcPowerMeasure(v) : null;

        switch (element)
        {
            case IfcAirTerminal:
                AddPropertySet(model, element, "Pset_AirTerminalOccurrence", [("AirFlowRate", Flow(spec.AirflowLps))]);
                break;
            case IfcFan:
                AddPropertySet(model, element, "Pset_FanTypeCommon",
                    [("NominalAirFlowRate", Flow(spec.AirflowLps)), ("NominalPowerRate", Power(spec.PowerW))]);
                break;
            case IfcLightFixture:
                AddPropertySet(model, element, "Pset_LightFixtureTypeCommon", [("TotalWattage", Power(spec.PowerW))]);
                break;
        }

        AddPropertySet(model, element, IfcDeviceAdapter.DataPset,
        [
            ("ConnectionSizeMm", spec.ConnectionSizeMm is { } c ? new IfcInteger(c) : null),
            ("WeightKg", spec.WeightKg is { } w ? new IfcReal(w) : null),
        ]);
    }

    /// <summary>Adds a property set with the given values; skips it entirely when every value is missing.</summary>
    private static void AddPropertySet(IModel model, IfcElement element, string name, IEnumerable<(string Name, IfcValue? Value)> values)
    {
        if (values.All(v => v.Value is null))
            return;

        var pset = model.Instances.New<IfcPropertySet>(p => p.Name = name);
        foreach (var (propertyName, value) in values.Where(v => v.Value is not null))
        {
            pset.HasProperties.Add(model.Instances.New<IfcPropertySingleValue>(p =>
            {
                p.Name = propertyName;
                p.NominalValue = value;
            }));
        }
        model.Instances.New<IfcRelDefinesByProperties>(r =>
        {
            r.RelatingPropertyDefinition = pset;
            r.RelatedObjects.Add(element);
        });
    }
}
