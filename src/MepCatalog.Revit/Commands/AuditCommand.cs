using System.Diagnostics;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MepCatalog.Client;
using MepCatalog.Core.Auditing;
using MepCatalog.Reporting;

namespace MepCatalog.Revit.Commands;

/// <summary>Audits the open model against the catalog and offers the next steps in one dialog.</summary>
[Transaction(TransactionMode.Manual)]
public class AuditCommand : IExternalCommand
{
    private const string Title = "MepCatalog";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        if (commandData.Application.ActiveUIDocument is not { } uiDoc)
        {
            message = "Open a project first.";
            return Result.Failed;
        }

        var doc = uiDoc.Document;
        var settings = AddinSettings.Load();
        var devices = RevitDevices.Read(doc);
        if (devices.Count == 0)
        {
            TaskDialog.Show(Title, "This model has no air terminals, mechanical equipment, duct accessories or light fixtures.");
            return Result.Succeeded;
        }

        IReadOnlyList<DeviceAuditResult> results;
        try
        {
            // The devices are plain records by now, so the HTTP calls can run off Revit's UI thread.
            // Waiting on them here keeps the Revit API calls that follow on the thread Revit requires.
            results = Task.Run(() => AuditAsync(settings.ApiUrl, devices)).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            TaskDialog.Show(Title,
                $"Could not reach the catalog at {settings.ApiUrl}.\n\n{ex.Message}\n\nUse MepCatalog → Settings to change the address.");
            return Result.Cancelled;
        }

        return ShowResults(uiDoc, results, settings.ApiUrl);
    }

    private static async Task<IReadOnlyList<DeviceAuditResult>> AuditAsync(string apiUrl, IReadOnlyList<ModelDevice> devices)
    {
        using var http = new HttpClient { BaseAddress = new Uri(apiUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(30) };
        return await new DeviceAuditor(new HttpProductCatalog(http)).AuditAsync(devices);
    }

    private static Result ShowResults(UIDocument uiDoc, IReadOnlyList<DeviceAuditResult> results, string apiUrl)
    {
        var doc = uiDoc.Document;
        var ok = results.Count(r => r.Status == AuditStatus.Ok);
        var fixable = results.Where(r => r.CanAutoFix).ToList();
        var needDesigner = results.Where(r => AuditStatusText.NeedsDesigner(r.Status)).ToList();

        var dialog = new TaskDialog(Title)
        {
            MainInstruction = $"{ok} of {results.Count} devices match the catalog",
            MainContent = $"{fixable.Count} can be filled in from the catalog automatically.\n"
                + $"{needDesigner.Count} need a designer's decision (no product, unknown product or wrong product type).",
            FooterText = $"Catalog: {apiUrl}",
            CommonButtons = TaskDialogCommonButtons.Close,
        };
        if (fixable.Count > 0)
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, $"Fill {fixable.Count} devices from the catalog",
                "Writes airflow, power, connection size and weight into the MC_ parameters. One Undo reverts it.");
        if (needDesigner.Count > 0)
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, $"Select the {needDesigner.Count} devices that need a designer",
                "Selects them in the model so you can find and fix them.");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Save Excel report",
            "Summary, all devices, a designer to-do list and every value change.");

        switch (dialog.Show())
        {
            case TaskDialogResult.CommandLink1:
                return Fill(doc, fixable);
            case TaskDialogResult.CommandLink2:
                uiDoc.Selection.SetElementIds(RevitDevices.ElementIds(doc, needDesigner));
                return Result.Succeeded;
            case TaskDialogResult.CommandLink3:
                SaveReport(doc, results, apiUrl);
                return Result.Succeeded;
            default:
                return Result.Succeeded;
        }
    }

    private static Result Fill(Document doc, IReadOnlyList<DeviceAuditResult> fixable)
    {
        if (!CatalogParameters.AreInstalled(doc))
        {
            TaskDialog.Show(Title, "This project doesn't have the MC_ catalog parameters yet.\n\nRun MepCatalog → Set up parameters first, then audit again.");
            return Result.Cancelled;
        }

        var changed = RevitDevices.ApplyCatalogValues(doc, fixable);
        TaskDialog.Show(Title, $"Filled {changed} devices from the catalog.\n\nUse Undo to revert, or audit again to check the result.");
        return Result.Succeeded;
    }

    private static void SaveReport(Document doc, IReadOnlyList<DeviceAuditResult> results, string apiUrl)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MepCatalog");
        Directory.CreateDirectory(folder);
        var name = string.Join("_", doc.Title.Split(Path.GetInvalidFileNameChars()));
        var path = Path.Combine(folder, $"{name}-audit-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");

        File.WriteAllBytes(path, ExcelAuditReport.Create(new AuditReportInfo(doc.Title, DateTime.UtcNow, apiUrl), results));
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
