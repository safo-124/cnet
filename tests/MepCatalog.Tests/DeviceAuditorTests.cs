using MepCatalog.Core;
using MepCatalog.Core.Auditing;

namespace MepCatalog.Tests;

public class DeviceAuditorTests
{
    private static readonly Product Diffuser = new()
    {
        Id = 1, Manufacturer = "Nordic Air Oy", Model = "KA-160", Category = ProductCategory.SupplyAirTerminal,
        AirflowLps = 50, ConnectionSizeMm = 160, WeightKg = 1.6,
    };

    private readonly DeviceAuditor _auditor = new(new FakeCatalog(Diffuser));

    private static ModelDevice AirTerminal(string? manufacturer = "Nordic Air Oy", string? model = "KA-160") => new()
    {
        Id = "guid-1",
        ElementType = "IfcAirTerminal",
        ExpectedCategories = [ProductCategory.SupplyAirTerminal, ProductCategory.ExhaustAirTerminal],
        Manufacturer = manufacturer,
        Model = model,
    };

    [Fact]
    public async Task Matching_values_are_ok()
    {
        var device = AirTerminal() with { AirflowLps = 50.004, ConnectionSizeMm = 160, WeightKg = 1.6 };

        var result = await _auditor.AuditAsync(device);

        Assert.Equal(AuditStatus.Ok, result.Status);
        Assert.Empty(result.Changes);
    }

    [Fact]
    public async Task Missing_and_different_values_need_update()
    {
        var device = AirTerminal() with { AirflowLps = 45 };

        var result = await _auditor.AuditAsync(device);

        Assert.Equal(AuditStatus.NeedsUpdate, result.Status);
        Assert.True(result.CanAutoFix);
        Assert.Collection(result.Changes,
            c => Assert.Equal(new FieldChange("AirflowLps", 45, 50), c),
            c => Assert.Equal(new FieldChange("ConnectionSizeMm", null, 160), c),
            c => Assert.Equal(new FieldChange("WeightKg", null, 1.6), c));
        Assert.Equal("2 value(s) missing, 1 differ from the catalog.", result.Message);
    }

    [Fact]
    public async Task Fields_the_catalog_lacks_are_not_reported()
    {
        // The diffuser has no power value in the catalog, so a power value in the model is left alone.
        var device = AirTerminal() with { AirflowLps = 50, ConnectionSizeMm = 160, WeightKg = 1.6, PowerW = 999 };

        var result = await _auditor.AuditAsync(device);

        Assert.Equal(AuditStatus.Ok, result.Status);
    }

    [Theory]
    [InlineData(null, "KA-160")]
    [InlineData("Nordic Air Oy", " ")]
    public async Task Device_without_identity_is_unidentified(string? manufacturer, string? model)
    {
        var result = await _auditor.AuditAsync(AirTerminal(manufacturer, model));

        Assert.Equal(AuditStatus.Unidentified, result.Status);
        Assert.False(result.CanAutoFix);
    }

    [Fact]
    public async Task Unknown_product_is_not_in_catalog()
    {
        var result = await _auditor.AuditAsync(AirTerminal(model: "KA-999"));

        Assert.Equal(AuditStatus.NotInCatalog, result.Status);
    }

    [Fact]
    public async Task Product_of_wrong_kind_is_a_category_mismatch()
    {
        var fan = AirTerminal() with { ElementType = "IfcFan", ExpectedCategories = [ProductCategory.Fan] };

        var result = await _auditor.AuditAsync(fan);

        Assert.Equal(AuditStatus.CategoryMismatch, result.Status);
        Assert.False(result.CanAutoFix);
    }

    private sealed class FakeCatalog(params Product[] products) : IProductCatalog
    {
        public Task<Product?> FindAsync(string manufacturer, string model, CancellationToken ct = default) =>
            Task.FromResult(products.FirstOrDefault(p =>
                p.Manufacturer.Equals(manufacturer, StringComparison.OrdinalIgnoreCase) &&
                p.Model.Equals(model, StringComparison.OrdinalIgnoreCase)));
    }
}
