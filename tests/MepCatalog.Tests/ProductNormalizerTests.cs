using MepCatalog.Core;

namespace MepCatalog.Tests;

public class ProductNormalizerTests
{
    [Theory]
    [InlineData("Tuloilmalaite", ProductCategory.SupplyAirTerminal)]
    [InlineData("  supply   diffuser ", ProductCategory.SupplyAirTerminal)]
    [InlineData("poistoilmalaite", ProductCategory.ExhaustAirTerminal)]
    [InlineData("Puhallin", ProductCategory.Fan)]
    [InlineData("Palopelti", ProductCategory.Damper)]
    [InlineData("Valaisin", ProductCategory.LightFixture)]
    [InlineData("LightFixture", ProductCategory.LightFixture)]
    [InlineData("Ilmastointikone", ProductCategory.Unknown)]
    public void Category_aliases_are_recognised(string input, ProductCategory expected)
    {
        Assert.Equal(expected, ProductCategoryParser.Parse(input));
    }

    [Fact]
    public void Valid_row_is_cleaned_and_converted()
    {
        var row = new RawProductRow("  Nordic   Air Oy ", "ka-160", "Tuloilmalaite", "Diffuser", "180 m3/h", null, "Ø160", "1,6 kg");

        var result = ProductNormalizer.Normalize(row);

        Assert.True(result.Success);
        var product = result.Product!;
        Assert.Equal("Nordic Air Oy", product.Manufacturer);
        Assert.Equal("KA-160", product.Model);
        Assert.Equal(ProductCategory.SupplyAirTerminal, product.Category);
        Assert.Equal(50, product.AirflowLps);
        Assert.Null(product.PowerW);
        Assert.Equal(160, product.ConnectionSizeMm);
        Assert.Equal(1.6, product.WeightKg);
    }

    [Fact]
    public void All_problems_in_a_row_are_reported_together()
    {
        var row = new RawProductRow("", null, "Something", null, "lots", "1 hp", null, null);

        var result = ProductNormalizer.Normalize(row);

        Assert.False(result.Success);
        Assert.Equal(5, result.Errors.Count);
    }
}
