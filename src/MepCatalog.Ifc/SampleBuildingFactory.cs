using Xbim.Common;
using Xbim.Common.Step21;
using Xbim.Ifc;
using Xbim.Ifc4.ElectricalDomain;
using Xbim.Ifc4.GeometricConstraintResource;
using Xbim.Ifc4.SharedBldgElements;
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

    private enum Shape { CeilingDiffuser, DuctFan, RoundDamper, Panel, Downlight, LinearLight }

    /// <param name="X">Position on the floor plan in mm (the building is 24 000 × 14 000 mm).</param>
    /// <param name="Y">Position on the floor plan in mm.</param>
    private record DeviceSpec(
        string Level, string Name, Shape Shape, double X, double Y,
        string? Manufacturer, string? Model,
        double? AirflowLps = null, double? PowerW = null, int? ConnectionSizeMm = null, double? WeightKg = null);

    private static readonly DeviceSpec[] Devices =
    [
        // Level 1: offices and meeting room along the south side, WC and kitchen to the north-east
        new("Level 1", "AT-101 Office 101 supply", Shape.CeilingDiffuser, 3000, 3000, "Nordic Air Oy", "KA-125", 35, null, 125, 1.2),
        new("Level 1", "AT-102 Office 102 supply", Shape.CeilingDiffuser, 9000, 3000, "Nordic Air Oy", "KA-160"),
        new("Level 1", "AT-103 Meeting room supply", Shape.CeilingDiffuser, 15000, 3000, "Nordic Air Oy", "KA-160", 50, null, 160, 1.6),
        new("Level 1", "AT-104 WC exhaust", Shape.CeilingDiffuser, 21000, 11000, "Nordic Air Oy", "KP-125"),
        new("Level 1", "AT-105 Corridor supply", Shape.CeilingDiffuser, 12000, 7000, null, null, 40),
        new("Level 1", "FAN-101 Kitchen exhaust fan", Shape.DuctFan, 17500, 11000, "VentoTech", "VT-EC 315", 700, 310, 315, 9.8),
        new("Level 1", "DMP-101 Fire damper shaft A", Shape.RoundDamper, 22500, 7000, "VentoTech", "FD-200", null, null, 200),
        new("Level 1", "LT-101 Open office panel", Shape.Panel, 6000, 10500, "Lumo Lighting", "LX-600 Panel"),
        // Level 2
        new("Level 2", "AT-201 Office 201 supply", Shape.CeilingDiffuser, 3000, 3000, "Nordic Air Oy", "KA-200"),
        new("Level 2", "AT-202 Server room supply", Shape.CeilingDiffuser, 9000, 3000, "Nordic Air Oy", "KA-315", 150, null, 315),
        new("Level 2", "AT-203 Storage exhaust", Shape.CeilingDiffuser, 15000, 11000, "nordic air oy", "kp-160"),
        new("Level 2", "FAN-201 Toilet exhaust fan", Shape.DuctFan, 20000, 11000, "Nordic Air Oy", "KA-160"),
        new("Level 2", "LT-201 Lobby downlight", Shape.Downlight, 4000, 10500, "Lumo Lighting", "LX-Downlight 18", null, 18, null, 0.4),
        new("Level 2", "LT-202 Corridor linear light", Shape.LinearLight, 12000, 7000, "Lumo Lighting", "LX-Line 1500", null, 40, null, 2.7),
    ];

    // Building dimensions in mm.
    private const double Length = 24000, Width = 14000, StoreyHeight = 3500, SlabThickness = 200, WallThickness = 200;
    private const double CeilingHeight = 2700; // devices hang from a suspended ceiling, below the next slab

    public static void Create(string path)
    {
        using var model = IfcStore.Create(Credentials, XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel);
        using (var txn = model.BeginTransaction("Create sample building"))
        {
            var geometry = new SampleGeometry(model);
            var project = model.Instances.New<IfcProject>(p => p.Name = "Demo Office Building");
            project.UnitsInContext = CreateUnits(model);
            project.RepresentationContexts.Add(geometry.Context);

            var site = model.Instances.New<IfcSite>(s =>
            {
                s.Name = "Demo Site";
                s.ObjectPlacement = geometry.Place(null, 0, 0, 0);
            });
            var building = model.Instances.New<IfcBuilding>(b =>
            {
                b.Name = "Office A";
                b.ObjectPlacement = geometry.Place(site.ObjectPlacement, 0, 0, 0);
            });
            Aggregate(model, project, site);
            Aggregate(model, site, building);

            var levels = Devices.Select(d => d.Level).Distinct().ToList();
            foreach (var (level, index) in levels.Select((level, i) => (level, i)))
            {
                var storey = model.Instances.New<IfcBuildingStorey>(s =>
                {
                    s.Name = level;
                    s.Elevation = index * StoreyHeight;
                    s.ObjectPlacement = geometry.Place(building.ObjectPlacement, 0, 0, index * StoreyHeight);
                });
                Aggregate(model, building, storey);

                var contained = model.Instances.New<IfcRelContainedInSpatialStructure>(r => r.RelatingStructure = storey);
                foreach (var element in BuildingElements(model, geometry, storey.ObjectPlacement))
                    contained.RelatedElements.Add(element);

                foreach (var spec in Devices.Where(d => d.Level == level))
                {
                    var element = CreateDevice(model, geometry, storey.ObjectPlacement, spec);
                    contained.RelatedElements.Add(element);
                    AddManufacturerInfo(model, element, spec);
                    AddProductData(model, element, spec);
                }
            }

            txn.Commit();
        }

        model.SaveAs(path);
    }

    /// <summary>A floor slab, four exterior walls and two interior walls that suggest the offices and the corridor.</summary>
    private static IEnumerable<IfcElement> BuildingElements(IModel model, SampleGeometry geometry, IfcObjectPlacement storey)
    {
        yield return model.Instances.New<IfcSlab>(s =>
        {
            s.Name = "Floor slab";
            s.PredefinedType = IfcSlabTypeEnum.FLOOR;
            s.ObjectPlacement = geometry.Place(storey, Length / 2, Width / 2, -SlabThickness);
            s.Representation = geometry.Box(Length, Width, SlabThickness);
        });

        const double wallHeight = StoreyHeight - SlabThickness;
        (string Name, double X, double Y, double W, double D)[] walls =
        [
            ("Exterior wall south", Length / 2, WallThickness / 2, Length, WallThickness),
            ("Exterior wall north", Length / 2, Width - WallThickness / 2, Length, WallThickness),
            ("Exterior wall west", WallThickness / 2, Width / 2, WallThickness, Width),
            ("Exterior wall east", Length - WallThickness / 2, Width / 2, WallThickness, Width),
            ("Corridor wall", Length / 2 - 2000, 5500, Length - 4000, 100),
            ("Office partition", 6000, 2750, 100, 5500),
        ];
        foreach (var wall in walls)
        {
            yield return model.Instances.New<IfcWall>(w =>
            {
                w.Name = wall.Name;
                w.ObjectPlacement = geometry.Place(storey, wall.X, wall.Y, 0);
                w.Representation = geometry.Box(wall.W, wall.D, wallHeight);
            });
        }
    }

    private static IfcElement CreateDevice(IModel model, SampleGeometry geometry, IfcObjectPlacement storey, DeviceSpec spec)
    {
        var size = spec.ConnectionSizeMm ?? 160;
        IfcElement element = spec.Shape switch
        {
            Shape.CeilingDiffuser => model.Instances.New<IfcAirTerminal>(),
            Shape.DuctFan => model.Instances.New<IfcFan>(),
            Shape.RoundDamper => model.Instances.New<IfcDamper>(),
            _ => model.Instances.New<IfcLightFixture>(),
        };
        element.Name = spec.Name;

        // (shape, height of its lowest point above the storey floor)
        var (representation, z) = spec.Shape switch
        {
            Shape.CeilingDiffuser => (geometry.Box(600, 600, 60), CeilingHeight - 60),
            Shape.Panel => (geometry.Box(600, 600, 60), CeilingHeight - 60),
            Shape.LinearLight => (geometry.Box(1500, 100, 70), CeilingHeight - 70),
            Shape.Downlight => (geometry.VerticalCylinder(200, 90), CeilingHeight - 90),
            // Duct fans and dampers sit in the duct above the ceiling, centred on the duct axis.
            Shape.DuctFan => (geometry.HorizontalCylinder(size + 100, 600), CeilingHeight + 300),
            _ => (geometry.HorizontalCylinder(size, 300), CeilingHeight + 300),
        };
        element.ObjectPlacement = geometry.Place(storey, spec.X, spec.Y, z);
        element.Representation = representation;
        return element;
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
