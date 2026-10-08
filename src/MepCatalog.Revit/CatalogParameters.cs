using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using MepCatalog.Core;

namespace MepCatalog.Revit;

/// <summary>
/// The project parameters that hold catalog values on each device, prefixed MC_ so they are easy to find in Revit.
/// To audit an IFC export of the model, map them in Revit's IFC exporter (user-defined property sets) to the standard
/// IFC4 properties the IFC auditor reads, or to its MepCatalog_ProductData set without the MC_ prefix.
/// </summary>
internal static class CatalogParameters
{
    private const string SharedParameterGroup = "MepCatalog";

    /// <summary>
    /// Shared parameters are identified by GUID, not name. Fixed GUIDs mean every project set up by the
    /// add-in gets the same parameters, so schedules, tags and IFC mappings work across projects.
    /// </summary>
    private static readonly (string Name, Guid Guid, string Description, bool IsInteger)[] Definitions =
    [
        (NameFor(nameof(Product.AirflowLps)), new("3f1d0c52-8a8e-4c0b-9d0a-1e6a1b7d2c01"), "Nominal airflow from the product catalog, l/s", false),
        (NameFor(nameof(Product.PowerW)), new("3f1d0c52-8a8e-4c0b-9d0a-1e6a1b7d2c02"), "Electrical power from the product catalog, W", false),
        (NameFor(nameof(Product.ConnectionSizeMm)), new("3f1d0c52-8a8e-4c0b-9d0a-1e6a1b7d2c03"), "Duct connection diameter from the product catalog, mm", false),
        (NameFor(nameof(Product.WeightKg)), new("3f1d0c52-8a8e-4c0b-9d0a-1e6a1b7d2c04"), "Weight from the product catalog, kg", false),
        (CatalogProductId, new("3f1d0c52-8a8e-4c0b-9d0a-1e6a1b7d2c05"), "Id of the catalog product the values came from", true),
    ];

    public const string CatalogProductId = "MC_CatalogProductId";

    /// <summary>Device categories the catalog covers, with how they are shown and which products fit them.</summary>
    public static readonly IReadOnlyDictionary<BuiltInCategory, (string Label, ProductCategory[] Expected)> DeviceCategories =
        new Dictionary<BuiltInCategory, (string, ProductCategory[])>
        {
            [BuiltInCategory.OST_DuctTerminal] = ("Air terminal", [ProductCategory.SupplyAirTerminal, ProductCategory.ExhaustAirTerminal]),
            [BuiltInCategory.OST_MechanicalEquipment] = ("Mechanical equipment", [ProductCategory.Fan]),
            [BuiltInCategory.OST_DuctAccessory] = ("Duct accessory", [ProductCategory.Damper]),
            [BuiltInCategory.OST_LightingFixtures] = ("Light fixture", [ProductCategory.LightFixture]),
        };

    /// <summary>Catalog field name, e.g. "AirflowLps", to Revit parameter name, e.g. "MC_AirflowLps".</summary>
    public static string NameFor(string field) => "MC_" + field;

    public static bool AreInstalled(Document doc) => MissingNames(doc).Count == 0;

    /// <summary>
    /// Adds the catalog parameters to the project as instance parameters on the device categories.
    /// Parameters that already exist are left alone. Returns the names that were added.
    /// </summary>
    public static IReadOnlyList<string> Install(Document doc, Application app)
    {
        var missing = MissingNames(doc);
        if (missing.Count == 0)
            return [];

        // Revit creates shared parameters through a shared parameter file. Use a private one and put the
        // user's own file back afterwards, so their setup is never changed.
        var originalFile = app.SharedParametersFilename;
        var file = Path.Combine(Path.GetTempPath(), "MepCatalog-SharedParameters.txt");
        if (!File.Exists(file))
            File.WriteAllText(file, "");

        try
        {
            app.SharedParametersFilename = file;
            var definitionFile = app.OpenSharedParameterFile();
            var group = definitionFile.Groups.get_Item(SharedParameterGroup)
                ?? definitionFile.Groups.Create(SharedParameterGroup);

            var categories = app.Create.NewCategorySet();
            foreach (var category in DeviceCategories.Keys)
                categories.Insert(Category.GetCategory(doc, category));

            using var tx = new Transaction(doc, "MepCatalog: add catalog parameters");
            tx.Start();
            foreach (var (name, guid, description, isInteger) in Definitions.Where(d => missing.Contains(d.Name)))
            {
                var definition = group.Definitions.get_Item(name)
                    ?? group.Definitions.Create(new ExternalDefinitionCreationOptions(name, isInteger ? SpecTypeId.Int.Integer : SpecTypeId.Number)
                    {
                        GUID = guid,
                        Description = description,
                        UserModifiable = true,
                    });
                doc.ParameterBindings.Insert(definition, app.Create.NewInstanceBinding(categories), GroupTypeId.Data);
            }
            tx.Commit();
        }
        finally
        {
            app.SharedParametersFilename = originalFile;
        }

        return missing.ToList();
    }

    private static HashSet<string> MissingNames(Document doc)
    {
        var existing = new HashSet<string>();
        var bindings = doc.ParameterBindings.ForwardIterator();
        while (bindings.MoveNext())
            existing.Add(((Definition)bindings.Key).Name);
        return Definitions.Select(d => d.Name).Where(n => !existing.Contains(n)).ToHashSet();
    }
}
