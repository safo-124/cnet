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
using Xbim.Ifc4.RepresentationResource;
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
        new("Level 1", "AT-105 Corridor supply", Shape.CeilingDiffuser, 12000, 6600, null, null, 40),
        new("Level 1", "FAN-101 Kitchen exhaust fan", Shape.DuctFan, 17500, 11000, "VentoTech", "VT-EC 315", 700, 310, 315, 9.8),
        new("Level 1", "DMP-101 Fire damper shaft A", Shape.RoundDamper, 22500, 6600, "VentoTech", "FD-200", null, null, 200),
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
            IfcRelContainedInSpatialStructure? topStorey = null;
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

                var devices = Devices.Where(d => d.Level == level).ToList();
                foreach (var spec in devices)
                {
                    var element = CreateDevice(model, geometry, storey.ObjectPlacement, spec);
                    contained.RelatedElements.Add(element);
                    AddManufacturerInfo(model, element, spec);
                    AddProductData(model, element, spec);
                }
                foreach (var duct in Ductwork(model, geometry, storey.ObjectPlacement, devices))
                    contained.RelatedElements.Add(duct);

                topStorey = contained;
            }

            // The roof closes the top storey.
            topStorey!.RelatedElements.Add(model.Instances.New<IfcSlab>(s =>
            {
                s.Name = "Roof slab";
                s.PredefinedType = IfcSlabTypeEnum.ROOF;
                s.ObjectPlacement = geometry.Place(((IfcBuildingStorey)topStorey.RelatingStructure).ObjectPlacement,
                    Length / 2, Width / 2, StoreyHeight - SlabThickness);
                s.Representation = geometry.Box(Length, Width, SlabThickness);
            }));

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

        // Ribbon windows in both long facades (sill at 900 mm), set into the wall plane.
        foreach (var (y, side) in new[] { (WallThickness / 2, "south"), (Width - WallThickness / 2, "north") })
        {
            foreach (var x in new[] { 3000.0, 9000, 15000, 21000 })
            {
                yield return model.Instances.New<IfcWindow>(w =>
                {
                    w.Name = $"Window {side} {x / 1000:0}";
                    w.OverallWidth = 2400;
                    w.OverallHeight = 1500;
                    w.ObjectPlacement = geometry.Place(storey, x, y, 900);
                    w.Representation = geometry.Box(2400, 80, 1500);
                });
            }
        }

        // Concrete columns along the north side of the corridor.
        foreach (var x in new[] { 6000.0, 12000, 18000 })
        {
            yield return model.Instances.New<IfcColumn>(c =>
            {
                c.Name = $"Column C{x / 1000:0}";
                c.ObjectPlacement = geometry.Place(storey, x, 8500, 0);
                c.Representation = geometry.Box(300, 300, wallHeight);
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

        // (shape, height of the placement above the storey floor)
        var (representation, z) = spec.Shape switch
        {
            // Face plate flush with the ceiling, round neck up to the branch duct.
            Shape.CeilingDiffuser => (geometry.Shape(
                geometry.BoxSolid(600, 600, 40),
                geometry.CylinderSolid(size, 150, SampleGeometry.Axis.Z, dz: 40)), CeilingHeight - 40),
            Shape.Panel => (geometry.Box(600, 600, 60), CeilingHeight - 60),
            Shape.LinearLight => (geometry.Box(1500, 100, 70), CeilingHeight - 70),
            Shape.Downlight => (geometry.Shape(geometry.CylinderSolid(200, 90, SampleGeometry.Axis.Z)), CeilingHeight - 90),
            // Fans and dampers sit in the duct above the ceiling; the placement is on the duct axis.
            Shape.DuctFan => (geometry.Shape(
                geometry.CylinderSolid(size + 100, 600, SampleGeometry.Axis.X),
                geometry.BoxSolid(260, 260, 200, dz: (size + 100) / 2.0 - 30)), DuctAxis),
            _ => (geometry.Shape(
                geometry.CylinderSolid(size, 300, SampleGeometry.Axis.X),
                geometry.BoxSolid(120, 120, 120, dy: size / 2.0 + 60, dz: -60)), DuctAxis),
        };
        element.ObjectPlacement = geometry.Place(storey, spec.X, spec.Y, z);
        element.Representation = representation;
        return element;
    }

    // Ductwork runs in the ceiling void: both mains along the corridor, their axis 300 mm above the ceiling.
    private const double DuctAxis = CeilingHeight + 300, SupplyMainY = 6600, ExhaustMainY = 7400;

    /// <summary>
    /// A supply and an exhaust main along the corridor, and a round branch from the right main to every air
    /// terminal and fan on the level: across to the device, then down to a diffuser's neck.
    /// </summary>
    private static IEnumerable<IfcElement> Ductwork(IModel model, SampleGeometry geometry, IfcObjectPlacement storey, IReadOnlyList<DeviceSpec> devices)
    {
        static bool IsExhaust(DeviceSpec d) => d.Name.Contains("exhaust", StringComparison.OrdinalIgnoreCase);

        IfcDuctSegment Duct(string name, IfcProductDefinitionShape shape) => model.Instances.New<IfcDuctSegment>(d =>
        {
            d.Name = name;
            d.PredefinedType = IfcDuctSegmentTypeEnum.RIGIDSEGMENT;
            d.ObjectPlacement = geometry.Place(storey, 0, 0, 0);
            d.Representation = shape;
        });

        // Rectangular mains, 300 × 250 mm. The supply main runs on to the fire damper at the shaft.
        var supplyEnd = devices.FirstOrDefault(d => d.Shape == Shape.RoundDamper)?.X - 150 ?? 21000;
        yield return Duct("Supply main", geometry.Shape(
            geometry.BoxSolid(supplyEnd - 1500, 300, 250, dx: (1500 + supplyEnd) / 2, dy: SupplyMainY, dz: DuctAxis - 125)));
        yield return Duct("Exhaust main", geometry.Shape(
            geometry.BoxSolid(21000 - 1500, 300, 250, dx: (1500 + 21000) / 2, dy: ExhaustMainY, dz: DuctAxis - 125)));

        foreach (var device in devices.Where(d => d.Shape is Shape.CeilingDiffuser or Shape.DuctFan))
        {
            var mainY = IsExhaust(device) ? ExhaustMainY : SupplyMainY;
            var size = device.ConnectionSizeMm ?? 160;
            var solids = new List<Xbim.Ifc4.GeometricModelResource.IfcExtrudedAreaSolid>();

            var run = Math.Abs(device.Y - mainY);
            if (run > 10)
                solids.Add(geometry.CylinderSolid(size, run, SampleGeometry.Axis.Y, device.X, (device.Y + mainY) / 2, DuctAxis));
            if (device.Shape == Shape.CeilingDiffuser)
                solids.Add(geometry.CylinderSolid(size, DuctAxis - (CeilingHeight + 150), SampleGeometry.Axis.Z, device.X, device.Y, CeilingHeight + 150));

            if (solids.Count > 0)
                yield return Duct($"Branch to {device.Name.Split(' ')[0]}", geometry.Shape([.. solids]));
        }
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
