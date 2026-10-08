using ClosedXML.Excel;
using MepCatalog.Core;
using MepCatalog.Core.Auditing;
using MepCatalog.Reporting;

namespace MepCatalog.Tests;

public class ExcelAuditReportTests
{
    private static readonly AuditReportInfo Info = new("office.ifc", new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc), "test");

    private static ModelDevice Device(string name) => new()
    {
        Id = $"id-{name}",
        Name = name,
        ElementType = "IfcAirTerminal",
        ExpectedCategories = [ProductCategory.SupplyAirTerminal],
        Level = "Level 1",
        Manufacturer = "Nordic Air Oy",
        Model = "KA-160",
    };

    private static readonly Product Diffuser = new() { Id = 7, Manufacturer = "Nordic Air Oy", Model = "KA-160" };

    private static readonly DeviceAuditResult[] Results =
    [
        new(Device("AT-1"), AuditStatus.Ok, "Matches the catalog.", Diffuser, []),
        new(Device("AT-2"), AuditStatus.NeedsUpdate, "2 value(s) missing.", Diffuser,
            [new FieldChange("AirflowLps", null, 50), new FieldChange("WeightKg", 1.5, 1.6)]),
        new(Device("AT-3") with { Model = "KA-999" }, AuditStatus.NotInCatalog, "Not found.", null, []),
    ];

    private static XLWorkbook Open(byte[] bytes) => new(new MemoryStream(bytes));

    [Fact]
    public void Workbook_has_the_four_sheets()
    {
        using var workbook = Open(ExcelAuditReport.Create(Info, Results));

        Assert.Equal(
            ["Summary", "All devices", "Designer actions", "Value changes"],
            workbook.Worksheets.Select(ws => ws.Name));
    }

    [Fact]
    public void Summary_counts_each_status_and_totals_them()
    {
        using var workbook = Open(ExcelAuditReport.Create(Info, Results));
        var ws = workbook.Worksheet("Summary");

        var counts = ws.RangeUsed()!.Rows()
            .Where(r => AuditStatusText.DisplayOrder.Select(AuditStatusText.Label).Contains(r.Cell(1).GetString()))
            .ToDictionary(r => r.Cell(1).GetString(), r => r.Cell(2).GetValue<int>());

        Assert.Equal(1, counts["OK"]);
        Assert.Equal(1, counts["Needs update"]);
        Assert.Equal(1, counts["Not in catalog"]);
        Assert.Equal(0, counts["Unidentified"]);
        Assert.Equal(3, ws.Cell("B6").GetValue<int>());
    }

    [Fact]
    public void Device_and_change_sheets_are_filterable_tables_with_one_row_per_item()
    {
        using var workbook = Open(ExcelAuditReport.Create(Info, Results));

        var devices = workbook.Worksheet("All devices").Table("Devices");
        Assert.Equal(3, devices.DataRange.RowCount());
        Assert.True(devices.ShowAutoFilter);

        var changes = workbook.Worksheet("Value changes").Table("ValueChanges");
        Assert.Equal(2, changes.DataRange.RowCount());
        var missing = changes.DataRange.Row(1);
        Assert.True(missing.Cell(5).IsEmpty());
        Assert.Equal(50, missing.Cell(6).GetValue<double>());
        Assert.Equal("Missing", missing.Cell(7).GetString());
    }

    [Fact]
    public void Designer_actions_list_only_devices_a_person_must_fix()
    {
        using var workbook = Open(ExcelAuditReport.Create(Info, Results));

        var todo = workbook.Worksheet("Designer actions").Table("DesignerActions");
        var row = Assert.Single(todo.DataRange.Rows());
        Assert.Equal("KA-999", row.Cell(5).GetString());
        Assert.Equal(AuditStatusText.Action(AuditStatus.NotInCatalog), row.Cell(7).GetString());
    }

    [Fact]
    public void Clean_model_gets_friendly_messages_instead_of_empty_tables()
    {
        using var workbook = Open(ExcelAuditReport.Create(Info, [Results[0]]));

        Assert.StartsWith("No designer actions needed", workbook.Worksheet("Designer actions").Cell("A1").GetString());
        Assert.StartsWith("No values to change", workbook.Worksheet("Value changes").Cell("A1").GetString());
    }
}
