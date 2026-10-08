using System.Diagnostics;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MepCatalog.Revit.Commands;

/// <summary>Opens the settings file in the default text editor.</summary>
[Transaction(TransactionMode.ReadOnly)]
public class SettingsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var settings = AddinSettings.Load();
        TaskDialog.Show("MepCatalog",
            $"Catalog address: {settings.ApiUrl}\n\nThe settings file opens next. Change \"apiUrl\" (for example to "
            + "http://localhost:5236 for a local catalog), save it, and the next audit uses the new address.");
        Process.Start(new ProcessStartInfo(AddinSettings.FilePath) { UseShellExecute = true });
        return Result.Succeeded;
    }
}
