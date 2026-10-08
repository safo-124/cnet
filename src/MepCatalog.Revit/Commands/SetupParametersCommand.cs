using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MepCatalog.Revit.Commands;

/// <summary>Adds the MC_ catalog parameters to the open project.</summary>
[Transaction(TransactionMode.Manual)]
public class SetupParametersCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        if (commandData.Application.ActiveUIDocument?.Document is not { IsFamilyDocument: false } doc)
        {
            message = "Open a project (not a family) first.";
            return Result.Failed;
        }

        var added = CatalogParameters.Install(doc, commandData.Application.Application);
        TaskDialog.Show("MepCatalog", added.Count == 0
            ? "The catalog parameters are already set up in this project."
            : $"Added {added.Count} parameters to air terminals, mechanical equipment, duct accessories and light fixtures:\n\n"
              + string.Join("\n", added));
        return Result.Succeeded;
    }
}
