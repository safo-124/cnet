using System.Text.Json;

namespace MepCatalog.Revit;

/// <summary>User settings, kept in %AppData%\MepCatalog\revit-settings.json and created on first use.</summary>
internal sealed record AddinSettings(string ApiUrl)
{
    // The public demo answers read-only catalog lookups, so the add-in works out of the box.
    private static readonly AddinSettings Defaults = new("https://mepcatalog.135.181.93.156.nip.io");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MepCatalog", "revit-settings.json");

    public static AddinSettings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<AddinSettings>(File.ReadAllText(FilePath), Json) is { ApiUrl.Length: > 0 } settings)
                return settings;
        }
        catch (JsonException)
        {
            // A broken file falls back to the defaults below instead of breaking the add-in.
        }

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(Defaults, Json));
        return Defaults;
    }
}
