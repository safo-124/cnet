using MepCatalog.Core;
using MepCatalog.Core.Auditing;
using MepCatalog.Ifc;
using Xbim.Common;
using Xbim.Common.Step21;
using Xbim.Ifc;
using Xbim.Ifc4.HvacDomain;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.Kernel;
using Xbim.Ifc4.MeasureResource;
using Xbim.Ifc4.PropertyResource;
using Xbim.IO;

namespace MepCatalog.Tests;

public class IfcStandardPropertyTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mepcatalog-psets").FullName;

    // ---- Units -------------------------------------------------------------------------------------------

    [Fact]
    public void Without_declared_units_measures_are_si()
    {
        using var model = NewModel(_ => null);

        var units = IfcUnits.Of(model);

        Assert.Equal(1, units.FlowRateFactor);
        Assert.Equal(1, units.PowerFactor);
    }

    [Fact]
    public void Prefixed_si_units_are_scaled()
    {
        using var model = NewModel(m => Assign(m, Si(m, IfcUnitEnum.POWERUNIT, IfcSIUnitName.WATT, IfcSIPrefix.KILO)));

        Assert.Equal(1000, IfcUnits.Of(model).PowerFactor);
    }

    [Fact]
    public void Cubic_metres_per_hour_is_a_derived_unit_with_a_converted_hour()
    {
        using var model = NewModel(m =>
        {
            var hour = m.Instances.New<IfcConversionBasedUnit>(u =>
            {
                u.Name = "HOUR";
                u.UnitType = IfcUnitEnum.TIMEUNIT;
                u.Dimensions = m.Instances.New<IfcDimensionalExponents>(d => d.TimeExponent = 1);
                u.ConversionFactor = m.Instances.New<IfcMeasureWithUnit>(f =>
                {
                    f.ValueComponent = new IfcTimeMeasure(3600);
                    f.UnitComponent = Si(m, IfcUnitEnum.TIMEUNIT, IfcSIUnitName.SECOND);
                });
            });
            var perHour = m.Instances.New<IfcDerivedUnit>(u =>
            {
                u.UnitType = IfcDerivedUnitEnum.VOLUMETRICFLOWRATEUNIT;
                u.Elements.Add(m.Instances.New<IfcDerivedUnitElement>(e => { e.Unit = Si(m, IfcUnitEnum.VOLUMEUNIT, IfcSIUnitName.CUBIC_METRE); e.Exponent = 1; }));
                u.Elements.Add(m.Instances.New<IfcDerivedUnitElement>(e => { e.Unit = hour; e.Exponent = -1; }));
            });
            return Assign(m, perHour);
        });

        Assert.Equal(1.0 / 3600, IfcUnits.Of(model).FlowRateFactor, precision: 12);
    }

    [Fact]
    public void Prefix_on_a_cubic_unit_applies_to_each_dimension()
    {
        using var model = NewModel(m => Assign(m, Si(m, IfcUnitEnum.VOLUMEUNIT, IfcSIUnitName.CUBIC_METRE, IfcSIPrefix.DECI)));
        var cubicDecimetre = model.Instances.FirstOrDefault<IIfcSIUnit>();

        // 1 dm³ = 0.001 m³, not 0.1 m³.
        Assert.Equal(0.001, IfcUnits.ToSi(cubicDecimetre), precision: 12);
    }

    // ---- Standard property sets ----------------------------------------------------------------------------

    [Fact]
    public void Sample_model_stores_airflow_in_litres_per_second_in_the_standard_property()
    {
        var path = Sample();
        using var model = IfcStore.Open(path);

        var at101 = Element<IfcAirTerminal>(model, "AT-101");
        var airflow = Property(at101, "Pset_AirTerminalOccurrence", "AirFlowRate");

        Assert.IsType<IfcVolumetricFlowRateMeasure>(airflow);
        Assert.Equal(35, Convert.ToDouble(airflow!.Value));
        Assert.Equal(0.001, IfcUnits.Of(model).FlowRateFactor, precision: 12);
        Assert.Equal(35, IfcDeviceAdapter.ReadDevices(model).Single(d => d.Name!.StartsWith("AT-101")).AirflowLps);
    }

    [Fact]
    public void Fix_writes_to_standard_properties_and_leaves_no_stale_copy()
    {
        var path = Sample();
        var fixedPath = Path.Combine(_dir, "fixed.ifc");
        using (var model = IfcStore.Open(path, SampleBuildingFactory.Credentials))
        {
            var lx101 = IfcDeviceAdapter.ReadDevices(model).Single(d => d.Name!.StartsWith("LT-101"));
            var at101 = IfcDeviceAdapter.ReadDevices(model).Single(d => d.Name!.StartsWith("AT-101"));
            var product = new Product { Id = 9, Manufacturer = "x", Model = "x" };
            DeviceAuditResult Fix(ModelDevice d, params FieldChange[] changes) => new(d, AuditStatus.NeedsUpdate, "", product, changes);

            using (var txn = model.BeginTransaction("fix"))
            {
                IfcDeviceAdapter.ApplyCatalogValues(model,
                [
                    Fix(lx101, new FieldChange("PowerW", null, 28), new FieldChange("WeightKg", null, 3.2)),
                    Fix(at101, new FieldChange("AirflowLps", 35, 36)),
                ]);
                txn.Commit();
            }
            model.SaveAs(fixedPath);
        }

        using var reopened = IfcStore.Open(fixedPath);
        var light = Element<IIfcLightFixture>(reopened, "LT-101");
        var terminal = Element<IfcAirTerminal>(reopened, "AT-101");

        Assert.Equal(28, Convert.ToDouble(Property(light, "Pset_LightFixtureTypeCommon", "TotalWattage")!.Value));
        Assert.Equal(3.2, Convert.ToDouble(Property(light, IfcDeviceAdapter.DataPset, "WeightKg")!.Value));
        Assert.Equal(36, Convert.ToDouble(Property(terminal, "Pset_AirTerminalOccurrence", "AirFlowRate")!.Value), precision: 6);
        Assert.Null(Property(terminal, IfcDeviceAdapter.DataPset, "AirflowLps"));
    }

    [Fact]
    public void Values_only_in_the_mepcatalog_set_are_still_read()
    {
        using var model = NewModel(_ => null);
        using (var txn = model.BeginTransaction("legacy"))
        {
            var terminal = model.Instances.New<IfcAirTerminal>(t => t.Name = "AT-legacy");
            var pset = model.Instances.New<IfcPropertySet>(p =>
            {
                p.Name = IfcDeviceAdapter.DataPset;
                p.HasProperties.Add(model.Instances.New<IfcPropertySingleValue>(v => { v.Name = "AirflowLps"; v.NominalValue = new IfcReal(42); }));
            });
            model.Instances.New<IfcRelDefinesByProperties>(r => { r.RelatingPropertyDefinition = pset; r.RelatedObjects.Add(terminal); });
            txn.Commit();
        }

        Assert.Equal(42, IfcDeviceAdapter.ReadDevices(model).Single().AirflowLps);
    }

    // ---- Helpers ------------------------------------------------------------------------------------------

    private string Sample()
    {
        var path = Path.Combine(_dir, $"{Guid.NewGuid():N}.ifc");
        SampleBuildingFactory.Create(path);
        return path;
    }

    private static IfcStore NewModel(Func<IModel, IfcUnitAssignment?> units)
    {
        var model = IfcStore.Create(SampleBuildingFactory.Credentials, XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel);
        using var txn = model.BeginTransaction("setup");
        var project = model.Instances.New<IfcProject>(p => p.Name = "Test");
        project.UnitsInContext = units(model);
        txn.Commit();
        return model;
    }

    private static IfcUnitAssignment Assign(IModel model, IfcUnit unit) =>
        model.Instances.New<IfcUnitAssignment>(a => a.Units.Add(unit));

    private static IfcSIUnit Si(IModel model, IfcUnitEnum type, IfcSIUnitName name, IfcSIPrefix? prefix = null) =>
        model.Instances.New<IfcSIUnit>(u => { u.UnitType = type; u.Name = name; u.Prefix = prefix; });

    private static T Element<T>(IModel model, string namePrefix) where T : IIfcProduct =>
        model.Instances.OfType<T>().Single(e => e.Name.ToString()!.StartsWith(namePrefix));

    private static IIfcValue? Property(IIfcObject obj, string pset, string name) =>
        obj.IsDefinedBy.Select(r => r.RelatingPropertyDefinition).OfType<IIfcPropertySet>()
            .Where(p => p.Name == pset)
            .SelectMany(p => p.HasProperties.OfType<IIfcPropertySingleValue>())
            .FirstOrDefault(p => p.Name == name)?.NominalValue;

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
