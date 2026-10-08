using MepCatalog.Core;

namespace MepCatalog.Reporting;

internal static class FieldText
{
    public static (string Label, string Unit) For(string field) => field switch
    {
        nameof(Product.AirflowLps) => ("Airflow", "l/s"),
        nameof(Product.PowerW) => ("Power", "W"),
        nameof(Product.ConnectionSizeMm) => ("Connection size", "mm"),
        nameof(Product.WeightKg) => ("Weight", "kg"),
        _ => (field, ""),
    };
}
