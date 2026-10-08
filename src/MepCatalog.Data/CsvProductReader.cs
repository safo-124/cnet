using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using MepCatalog.Core;

namespace MepCatalog.Data;

/// <summary>
/// Reads manufacturer product CSV files. Detects ',' or ';' delimiters (Finnish Excel exports use ';')
/// and matches headers case-insensitively, ignoring spaces and underscores.
/// </summary>
public static class CsvProductReader
{
    public static List<RawProductRow> Read(TextReader reader)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            DetectDelimiter = true,
            PrepareHeaderForMatch = args => args.Header.Replace(" ", "").Replace("_", "").ToLowerInvariant(),
            HeaderValidated = null,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim,
        };

        using var csv = new CsvReader(reader, config);
        csv.Context.RegisterClassMap<RawProductRowMap>();
        return csv.GetRecords<RawProductRow>().ToList();
    }

    private sealed class RawProductRowMap : ClassMap<RawProductRow>
    {
        public RawProductRowMap()
        {
            Parameter(nameof(RawProductRow.Manufacturer)).Name("manufacturer", "valmistaja");
            Parameter(nameof(RawProductRow.Model)).Name("model", "malli", "productcode");
            Parameter(nameof(RawProductRow.Category)).Name("category", "type", "tyyppi");
            Parameter(nameof(RawProductRow.Description)).Name("description", "kuvaus").Optional();
            Parameter(nameof(RawProductRow.Airflow)).Name("airflow", "ilmavirta").Optional();
            Parameter(nameof(RawProductRow.Power)).Name("power", "teho").Optional();
            Parameter(nameof(RawProductRow.ConnectionSize)).Name("connectionsize", "connection", "liitäntäkoko").Optional();
            Parameter(nameof(RawProductRow.Weight)).Name("weight", "paino").Optional();
        }
    }
}
