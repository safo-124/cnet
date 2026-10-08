using System.Reflection;
using Autodesk.Revit.UI;
using MepCatalog.Revit.Commands;

namespace MepCatalog.Revit;

/// <summary>Adds the MepCatalog tab to the Revit ribbon when Revit starts.</summary>
public class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        const string tab = "MepCatalog";
        application.CreateRibbonTab(tab);
        var panel = application.CreateRibbonPanel(tab, "Product data");
        var assembly = Assembly.GetExecutingAssembly().Location;

        panel.AddItem(new PushButtonData("MepCatalog.Audit", "Audit\nmodel", assembly, typeof(AuditCommand).FullName)
        {
            ToolTip = "Check air terminals, fans, dampers and light fixtures against the product catalog.",
            LongDescription = "Reads the manufacturer and model of every device, looks them up in the MepCatalog catalog, "
                + "and offers to fill in missing or outdated values, select the devices that need a designer, "
                + "or save an Excel report.",
            LargeImage = Icons.Audit,
            Image = Icons.Audit,
        });

        panel.AddSeparator();

        panel.AddItem(new PushButtonData("MepCatalog.Parameters", "Set up\nparameters", assembly, typeof(SetupParametersCommand).FullName)
        {
            ToolTip = "Add the MC_ catalog parameters (airflow, power, connection size, weight) to this project.",
            LargeImage = Icons.Parameters,
            Image = Icons.Parameters,
        });

        panel.AddItem(new PushButtonData("MepCatalog.Settings", "Settings", assembly, typeof(SettingsCommand).FullName)
        {
            ToolTip = "Change which catalog the add-in uses (the catalog API address).",
            LargeImage = Icons.Settings,
            Image = Icons.Settings,
        });

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
}
