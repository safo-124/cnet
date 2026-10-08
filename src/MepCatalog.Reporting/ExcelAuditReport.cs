using ClosedXML.Excel;
using MepCatalog.Core.Auditing;

namespace MepCatalog.Reporting;

/// <param name="ModelName">File name of the audited model.</param>
/// <param name="GeneratedUtc">When the audit ran.</param>
/// <param name="CatalogSource">Where product data came from, e.g. the API address.</param>
public record AuditReportInfo(string ModelName, DateTime GeneratedUtc, string CatalogSource);

/// <summary>
/// Builds an Excel workbook from audit results: a summary, a filterable list of every device,
/// a to-do list for designers, and every value the catalog would change.
/// </summary>
public static class ExcelAuditReport
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly XLColor HeaderFill = XLColor.FromHtml("#1F2937");
    private static readonly XLColor OkFill = XLColor.FromHtml("#D1FAE5");
    private static readonly XLColor UpdateFill = XLColor.FromHtml("#FEF3C7");
    private static readonly XLColor DesignerFill = XLColor.FromHtml("#FEE2E2");

    public static byte[] Create(AuditReportInfo info, IReadOnlyList<DeviceAuditResult> results)
    {
        using var stream = new MemoryStream();
        Write(stream, info, results);
        return stream.ToArray();
    }

    public static void Write(Stream output, AuditReportInfo info, IReadOnlyList<DeviceAuditResult> results)
    {
        using var workbook = new XLWorkbook();
        AddSummary(workbook, info, results);
        AddDevices(workbook, results);
        AddDesignerActions(workbook, results);
        AddChanges(workbook, results);
        workbook.SaveAs(output);
    }

    private static void AddSummary(XLWorkbook workbook, AuditReportInfo info, IReadOnlyList<DeviceAuditResult> results)
    {
        var ws = workbook.Worksheets.Add("Summary");
        ws.Cell("A1").Value = "Model audit report";
        ws.Cell("A1").Style.Font.SetBold().Font.SetFontSize(16);

        (string Label, XLCellValue Value)[] details =
        [
            ("Model", info.ModelName),
            ("Generated (UTC)", info.GeneratedUtc.ToString("yyyy-MM-dd HH:mm")),
            ("Catalog", info.CatalogSource),
            ("Devices checked", results.Count),
        ];
        for (var i = 0; i < details.Length; i++)
        {
            ws.Cell(3 + i, 1).Value = details[i].Label;
            ws.Cell(3 + i, 1).Style.Font.SetBold();
            ws.Cell(3 + i, 2).Value = details[i].Value;
            ws.Cell(3 + i, 2).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
        }

        const int headerRow = 8;
        Header(ws.Range(headerRow, 1, headerRow, 3), "Status", "Devices", "What to do");

        var row = headerRow + 1;
        foreach (var status in AuditStatusText.DisplayOrder)
        {
            ws.Cell(row, 1).Value = AuditStatusText.Label(status);
            ws.Cell(row, 2).Value = results.Count(r => r.Status == status);
            ws.Cell(row, 3).Value = AuditStatusText.Action(status);
            ws.Range(row, 1, row, 3).Style.Fill.SetBackgroundColor(FillFor(status));
            row++;
        }
        ws.Cell(row, 1).Value = "Total";
        ws.Cell(row, 2).FormulaA1 = $"SUM(B{headerRow + 1}:B{row - 1})";
        ws.Range(row, 1, row, 2).Style.Font.SetBold();

        ws.Column(1).Width = 20;
        ws.Column(2).Width = 40;
        ws.Column(3).Width = 100;
        ws.Column(3).Style.Alignment.SetWrapText();
        // Column B is wide for the details block; keep the counts readable.
        ws.Range(headerRow + 1, 2, row, 2).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
    }

    private static void AddDevices(XLWorkbook workbook, IReadOnlyList<DeviceAuditResult> results)
    {
        var ws = workbook.Worksheets.Add("All devices");
        string[] headers = ["Level", "Device", "IFC type", "Manufacturer", "Model", "Status", "Details", "Catalog id", "GlobalId"];
        double[] widths = [10, 34, 18, 18, 18, 20, 60, 11, 26];

        var row = 2;
        foreach (var r in results)
        {
            ws.Cell(row, 1).Value = r.Device.Level;
            ws.Cell(row, 2).Value = r.Device.Name;
            ws.Cell(row, 3).Value = r.Device.ElementType;
            ws.Cell(row, 4).Value = r.Device.Manufacturer;
            ws.Cell(row, 5).Value = r.Device.Model;
            ws.Cell(row, 6).Value = AuditStatusText.Label(r.Status);
            ws.Cell(row, 6).Style.Fill.SetBackgroundColor(FillFor(r.Status));
            ws.Cell(row, 7).Value = r.Message;
            if (r.Product is not null)
                ws.Cell(row, 8).Value = r.Product.Id;
            ws.Cell(row, 9).Value = r.Device.Id;
            row++;
        }

        FinishTable(ws, "Devices", headers, widths, lastRow: row - 1);
    }

    private static void AddDesignerActions(XLWorkbook workbook, IReadOnlyList<DeviceAuditResult> results)
    {
        var ws = workbook.Worksheets.Add("Designer actions");
        var todo = results.Where(r => AuditStatusText.NeedsDesigner(r.Status)).ToList();
        if (todo.Count == 0)
        {
            ws.Cell("A1").Value = "No designer actions needed. Every device is in the catalog and of the right type.";
            ws.Column(1).Width = 90;
            return;
        }

        string[] headers = ["Done", "Level", "Device", "Manufacturer", "Model", "Problem", "What to do", "GlobalId"];
        double[] widths = [8, 10, 34, 18, 18, 50, 70, 26];

        var row = 2;
        foreach (var r in todo)
        {
            ws.Cell(row, 1).Value = "";
            ws.Cell(row, 2).Value = r.Device.Level;
            ws.Cell(row, 3).Value = r.Device.Name;
            ws.Cell(row, 4).Value = r.Device.Manufacturer;
            ws.Cell(row, 5).Value = r.Device.Model;
            ws.Cell(row, 6).Value = r.Message;
            ws.Cell(row, 7).Value = AuditStatusText.Action(r.Status);
            ws.Cell(row, 8).Value = r.Device.Id;
            row++;
        }

        FinishTable(ws, "DesignerActions", headers, widths, lastRow: row - 1);
        ws.Column(7).Style.Alignment.SetWrapText();
        // A simple checkbox column designers can fill in while working through the list.
        ws.Range(2, 1, row - 1, 1).CreateDataValidation().List("\"Yes,No\"");
    }

    private static void AddChanges(XLWorkbook workbook, IReadOnlyList<DeviceAuditResult> results)
    {
        var ws = workbook.Worksheets.Add("Value changes");
        string[] headers = ["Level", "Device", "Field", "Unit", "In model", "In catalog", "Change"];
        double[] widths = [10, 34, 18, 8, 12, 12, 12];

        var row = 2;
        foreach (var r in results)
        {
            foreach (var change in r.Changes)
            {
                var (label, unit) = FieldText.For(change.Field);
                ws.Cell(row, 1).Value = r.Device.Level;
                ws.Cell(row, 2).Value = r.Device.Name;
                ws.Cell(row, 3).Value = label;
                ws.Cell(row, 4).Value = unit;
                if (change.ModelValue is { } current)
                    ws.Cell(row, 5).Value = current;
                ws.Cell(row, 6).Value = change.CatalogValue;
                ws.Cell(row, 7).Value = change.ModelValue is null ? "Missing" : "Differs";
                ws.Cell(row, 7).Style.Fill.SetBackgroundColor(UpdateFill);
                row++;
            }
        }

        if (row == 2)
        {
            ws.Cell("A1").Value = "No values to change. Every catalog value is already in the model.";
            ws.Column(1).Width = 90;
            return;
        }

        FinishTable(ws, "ValueChanges", headers, widths, lastRow: row - 1);
        ws.Range(2, 5, row - 1, 6).Style.NumberFormat.Format = "0.###";
    }

    /// <summary>Writes the header, turns the range into an Excel table with filters, and freezes the header row.</summary>
    private static void FinishTable(IXLWorksheet ws, string name, string[] headers, double[] widths, int lastRow)
    {
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Column(i + 1).Width = widths[i];
        }

        var table = ws.Range(1, 1, lastRow, headers.Length).CreateTable(name);
        table.Theme = XLTableTheme.TableStyleLight1;
        Header(table.HeadersRow().AsRange(), headers);
        ws.SheetView.FreezeRows(1);
        ws.Rows(2, lastRow).Style.Alignment.SetVertical(XLAlignmentVerticalValues.Top);
    }

    private static void Header(IXLRange range, params string[] titles)
    {
        for (var i = 0; i < titles.Length; i++)
            range.Cell(1, i + 1).Value = titles[i];
        range.Style.Font.SetBold().Font.SetFontColor(XLColor.White);
        range.Style.Fill.SetBackgroundColor(HeaderFill);
    }

    private static XLColor FillFor(AuditStatus status) => status switch
    {
        AuditStatus.Ok => OkFill,
        AuditStatus.NeedsUpdate => UpdateFill,
        _ => DesignerFill,
    };
}
