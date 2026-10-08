using System.Globalization;
using System.Text.RegularExpressions;

namespace MepCatalog.Core;

/// <summary>
/// Parses free-text technical values from manufacturer data into canonical units.
/// Handles both decimal separators ("0.5" and "0,5") and common unit spellings.
/// </summary>
public static partial class UnitParser
{
    [GeneratedRegex(@"^\s*(?:Ø|ø|DN)?\s*(?<num>-?\d+(?:[.,]\d+)?)\s*(?<unit>[^\d\s].*?)?\s*$")]
    private static partial Regex ValueWithUnit();

    /// <summary>Airflow to l/s. Accepts l/s, m3/h, m³/h and m3/s. A bare number is taken as l/s.</summary>
    public static double? ParseAirflowLps(string? text) => Parse(text, "l/s", unit => unit switch
    {
        "l/s" or "ls" or "dm3/s" => 1.0,
        "m3/h" or "m³/h" or "cmh" => 1000.0 / 3600.0,
        "m3/s" or "m³/s" => 1000.0,
        _ => null,
    });

    /// <summary>Power to W. Accepts W and kW. A bare number is taken as W.</summary>
    public static double? ParsePowerW(string? text) => Parse(text, "w", unit => unit switch
    {
        "w" => 1.0,
        "kw" => 1000.0,
        _ => null,
    });

    /// <summary>Duct connection size to mm. Accepts "Ø160", "DN160", "160 mm" and "0.16 m".</summary>
    public static int? ParseConnectionSizeMm(string? text)
    {
        var mm = Parse(text, "mm", unit => unit switch
        {
            "mm" => 1.0,
            "cm" => 10.0,
            "m" => 1000.0,
            _ => null,
        });
        return mm is null ? null : (int)Math.Round(mm.Value);
    }

    /// <summary>Weight to kg. Accepts kg and g. A bare number is taken as kg.</summary>
    public static double? ParseWeightKg(string? text) => Parse(text, "kg", unit => unit switch
    {
        "kg" => 1.0,
        "g" => 0.001,
        _ => null,
    });

    private static double? Parse(string? text, string defaultUnit, Func<string, double?> factorFor)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = ValueWithUnit().Match(text);
        if (!match.Success)
            throw new FormatException($"Cannot read a number from '{text}'.");

        var number = double.Parse(match.Groups["num"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        var unit = match.Groups["unit"].Success
            ? match.Groups["unit"].Value.Replace(" ", "").ToLowerInvariant()
            : defaultUnit;

        var factor = factorFor(unit)
            ?? throw new FormatException($"Unknown unit '{match.Groups["unit"].Value}' in '{text}'.");

        return Math.Round(number * factor, 3);
    }
}
