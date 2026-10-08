using MepCatalog.Core;
using MepCatalog.Core.Auditing;
using MepCatalog.Ifc;
using Xbim.Ifc;

namespace MepCatalog.Tests;

/// <summary>Creates a real IFC file, audits it, writes fixes, saves, reopens and audits again.</summary>
public class IfcRoundTripTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mepcatalog-tests").FullName;

    private static readonly IProductCatalog Catalog = new InMemoryCatalog(
    [
        new() { Id = 1, Manufacturer = "Nordic Air Oy", Model = "KA-125", Category = ProductCategory.SupplyAirTerminal, AirflowLps = 36, ConnectionSizeMm = 125, WeightKg = 1.2 },
        new() { Id = 2, Manufacturer = "Nordic Air Oy", Model = "KA-160", Category = ProductCategory.SupplyAirTerminal, AirflowLps = 50, ConnectionSizeMm = 160, WeightKg = 1.6 },
        new() { Id = 3, Manufacturer = "Nordic Air Oy", Model = "KA-200", Category = ProductCategory.SupplyAirTerminal, AirflowLps = 90, ConnectionSizeMm = 200, WeightKg = 2.1 },
        new() { Id = 4, Manufacturer = "Nordic Air Oy", Model = "KP-125", Category = ProductCategory.ExhaustAirTerminal, AirflowLps = 30, ConnectionSizeMm = 125, WeightKg = 0.6 },
        new() { Id = 5, Manufacturer = "Nordic Air Oy", Model = "KP-160", Category = ProductCategory.ExhaustAirTerminal, AirflowLps = 45, ConnectionSizeMm = 160, WeightKg = 0.8 },
        new() { Id = 6, Manufacturer = "VentoTech", Model = "VT-EC 315", Category = ProductCategory.Fan, AirflowLps = 700, PowerW = 310, ConnectionSizeMm = 315, WeightKg = 9.8 },
        new() { Id = 7, Manufacturer = "VentoTech", Model = "FD-200", Category = ProductCategory.Damper, ConnectionSizeMm = 200, WeightKg = 3.4 },
        new() { Id = 8, Manufacturer = "Lumo Lighting", Model = "LX-600 PANEL", Category = ProductCategory.LightFixture, PowerW = 28, WeightKg = 3.2 },
        new() { Id = 9, Manufacturer = "Lumo Lighting", Model = "LX-DOWNLIGHT 18", Category = ProductCategory.LightFixture, PowerW = 18, WeightKg = 0.4 },
        new() { Id = 10, Manufacturer = "Lumo Lighting", Model = "LX-LINE 1500", Category = ProductCategory.LightFixture, PowerW = 42, WeightKg = 2.7 },
    ]);

    [Fact]
    public void Sample_model_devices_are_read_with_levels_and_values()
    {
        var path = CreateSample();
        using var model = IfcStore.Open(path);

        var devices = IfcDeviceAdapter.ReadDevices(model);

        Assert.Equal(14, devices.Count);
        Assert.Equal(4, devices.Select(d => d.ElementType).Distinct().Count());

        var at101 = devices.Single(d => d.Name!.StartsWith("AT-101"));
        Assert.Equal("Level 1", at101.Level);
        Assert.Equal("Nordic Air Oy", at101.Manufacturer);
        Assert.Equal("KA-125", at101.Model);
        Assert.Equal(35, at101.AirflowLps);
        Assert.Equal(125, at101.ConnectionSizeMm);
    }

    [Fact]
    public async Task Fixing_the_model_resolves_every_auto_fixable_device()
    {
        var path = CreateSample();
        var fixedPath = Path.Combine(_dir, "fixed.ifc");
        var auditor = new DeviceAuditor(Catalog);

        using (var model = IfcStore.Open(path, SampleBuildingFactory.Credentials))
        {
            var before = await auditor.AuditAsync(IfcDeviceAdapter.ReadDevices(model));
            Assert.Equal(8, before.Count(r => r.CanAutoFix));

            using (var txn = model.BeginTransaction("fix"))
            {
                Assert.Equal(8, IfcDeviceAdapter.ApplyCatalogValues(model, before));
                txn.Commit();
            }
            model.SaveAs(fixedPath);
        }

        using var reopened = IfcStore.Open(fixedPath);
        var after = await auditor.AuditAsync(IfcDeviceAdapter.ReadDevices(reopened));

        Assert.DoesNotContain(after, r => r.CanAutoFix);
        Assert.Equal(11, after.Count(r => r.Status == AuditStatus.Ok));
        Assert.Equal(3, after.Count(r => r.Status is AuditStatus.Unidentified or AuditStatus.NotInCatalog or AuditStatus.CategoryMismatch));

        var at101 = IfcDeviceAdapter.ReadDevices(reopened).Single(d => d.Name!.StartsWith("AT-101"));
        Assert.Equal(36, at101.AirflowLps);
    }

    private string CreateSample()
    {
        var path = Path.Combine(_dir, $"{Guid.NewGuid():N}.ifc");
        SampleBuildingFactory.Create(path);
        return path;
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class InMemoryCatalog(IReadOnlyList<Product> products) : IProductCatalog
    {
        public Task<Product?> FindAsync(string manufacturer, string model, CancellationToken ct = default) =>
            Task.FromResult(products.FirstOrDefault(p =>
                p.Manufacturer.Equals(manufacturer, StringComparison.OrdinalIgnoreCase) &&
                p.Model.Equals(model, StringComparison.OrdinalIgnoreCase)));
    }
}
