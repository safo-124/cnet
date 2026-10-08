# MepCatalog for Revit

A Revit 2026 add-in that checks a model's air terminals, mechanical equipment, duct accessories and light fixtures
against the MepCatalog product catalog, using the same audit rules as the IFC auditor and the web app.

```
Revit model ──► RevitDevices (adapter) ──► ModelDevice ──► DeviceAuditor (shared) ◄──► catalog REST API
                     ▲                                          │
                     └──── fill MC_ parameters ◄─── results ────┴──► select devices / Excel report
```

Only `RevitDevices.cs` and `CatalogParameters.cs` know about Revit. The audit rules (`MepCatalog.Core`), the API
client (`MepCatalog.Client`) and the Excel report (`MepCatalog.Reporting`) are the same tested code the other tools use.

## What it adds to Revit

A **MepCatalog** ribbon tab with:

| Button | What it does |
|---|---|
| **Audit model** | Reads every device's *Manufacturer* and *Model* (type parameters), looks them up in the catalog and shows how many match. Then: **fill** missing or outdated values (one undoable transaction), **select** the devices that need a designer, or **save an Excel report** to *Documents\MepCatalog*. |
| **Set up parameters** | Adds `MC_AirflowLps`, `MC_PowerW`, `MC_ConnectionSizeMm`, `MC_WeightKg` and `MC_CatalogProductId` as instance parameters on the four device categories. They are shared parameters with fixed GUIDs, so they're the same in every project and can be scheduled, tagged and exported to IFC. |
| **Settings** | Opens `%AppData%\MepCatalog\revit-settings.json` to change the catalog address. The default is the public demo, so the add-in works without running anything locally. |

## Install

Requires Revit 2026 and the .NET 8 SDK or newer.

```bash
dotnet build src/MepCatalog.Revit
```

On a machine with Revit 2026 the Debug build installs itself for the current user
(`%AppData%\Autodesk\Revit\Addins\2026`). Start Revit, allow the add-in when asked, and open the **MepCatalog** tab.

To try it: open any project with MEP families (Revit's sample *Snowdon Towers* models work), run **Set up
parameters**, set *Manufacturer* and *Model* on a few air terminal types to products in the catalog (for example
`Nordic Air Oy` / `KA-160`), then **Audit model**.

## Design notes

- **Threading.** Revit's API may only be used on Revit's own thread. The add-in reads the model into plain
  `ModelDevice` records first, runs the HTTP lookups on a background task, and does every write back on Revit's thread.
- **The user's shared parameter file is never changed.** Creating shared parameters needs a file; the add-in
  temporarily points Revit at its own and restores the user's setting afterwards.
- **Values are unitless numbers named with their unit** (`MC_AirflowLps`), so no conversion from Revit's internal
  units (feet, cubic feet per second) is needed for catalog values. For IFC export, map them in Revit's IFC exporter
  to the standard IFC4 properties the IFC auditor reads (`Pset_AirTerminalOccurrence.AirFlowRate` and so on).

## Status and limitations

- Builds against the official Revit 2026 API (reference packages) and is compiled on every CI run.
  **It has not yet been run inside Revit**; that needs a Revit installation.
- Revit 2027 runs on .NET 10. Supporting it means adding a `net10.0-windows` target with the 2027 API packages.
- *Mechanical equipment* is treated as fans. A real deployment would filter by family or type parameter, since the
  category also contains air handling units, pumps and so on.
