using System.Text;
using MepCatalog.Core.Auditing;

namespace MepCatalog.Reporting;

/// <summary>Plain CSV report for scripts and other tools. Uses ';' and a UTF-8 BOM so Finnish Excel opens it correctly.</summary>
public static class CsvAuditReport
{
    public static void Write(string path, IReadOnlyList<DeviceAuditResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("GlobalId;Level;Name;ElementType;Manufacturer;Model;Status;Message;Changes");
        foreach (var r in results)
        {
            var changes = string.Join(", ", r.Changes.Select(c =>
                $"{c.Field}: {c.ModelValue?.ToString() ?? "missing"} -> {c.CatalogValue}"));
            sb.AppendLine(string.Join(';', new object?[]
            {
                r.Device.Id, r.Device.Level, r.Device.Name, r.Device.ElementType,
                r.Device.Manufacturer, r.Device.Model, r.Status, r.Message, changes,
            }.Select(Cell)));
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string Cell(object? value)
    {
        var text = value?.ToString() ?? "";
        return text.IndexOfAny([';', '"', '\n']) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }
}
