using MepCatalog.Core;

namespace MepCatalog.Tests;

public class UnitParserTests
{
    [Theory]
    [InlineData("35 l/s", 35)]
    [InlineData("35", 35)]
    [InlineData("180 m3/h", 50)]
    [InlineData("108 m³/h", 30)]
    [InlineData("0,35 m3/s", 350)]
    [InlineData("1.1 m3/s", 1100)]
    public void Airflow_is_converted_to_litres_per_second(string input, double expected)
    {
        Assert.Equal(expected, UnitParser.ParseAirflowLps(input));
    }

    [Theory]
    [InlineData("310 W", 310)]
    [InlineData("18W", 18)]
    [InlineData("0,18 kW", 180)]
    [InlineData("0.75 kW", 750)]
    public void Power_is_converted_to_watts(string input, double expected)
    {
        Assert.Equal(expected, UnitParser.ParsePowerW(input));
    }

    [Theory]
    [InlineData("Ø125", 125)]
    [InlineData("DN160", 160)]
    [InlineData("200 mm", 200)]
    [InlineData("0.4 m", 400)]
    [InlineData("31,5 cm", 315)]
    public void Connection_size_is_converted_to_millimetres(string input, int expected)
    {
        Assert.Equal(expected, UnitParser.ParseConnectionSizeMm(input));
    }

    [Theory]
    [InlineData("1,2 kg", 1.2)]
    [InlineData("450 g", 0.45)]
    [InlineData("0.8", 0.8)]
    public void Weight_is_converted_to_kilograms(string input, double expected)
    {
        Assert.Equal(expected, UnitParser.ParseWeightKg(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_values_are_null(string? input)
    {
        Assert.Null(UnitParser.ParseAirflowLps(input));
    }

    [Theory]
    [InlineData("1,1 hp")]
    [InlineData("about forty")]
    public void Unreadable_values_throw(string input)
    {
        Assert.Throws<FormatException>(() => UnitParser.ParsePowerW(input));
    }
}
