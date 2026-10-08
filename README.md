# MepCatalog

[![CI](https://github.com/safo-124/cnet/actions/workflows/ci.yml/badge.svg)](https://github.com/safo-124/cnet/actions/workflows/ci.yml)

**A product catalog and BIM model auditor for building services (HVAC and electrical) design.**

Designers place hundreds of air terminals, fans, dampers and light fixtures in a building model, and each one should carry
correct product data: airflow, power, connection size, weight. Filling this in by hand is slow and error-prone, and the data
goes out of date when manufacturers update their datasheets.

MepCatalog keeps that product data in one central catalog and checks building models against it:

- **Catalog**: import messy manufacturer CSV files (mixed units, Finnish and English terms) or read **PDF datasheets with AI**, then review and save.
- **Audit**: open an IFC model, find every device with missing or outdated data, and fill it in from the catalog automatically.
- **Report**: give designers an Excel to-do list of the things that need a person's decision.

> All manufacturers, products and datasheets in this repository are fictional sample data.

![Model audit page](docs/screenshots/audit.png)

## Features

| | |
|---|---|
| **Catalog API** | ASP.NET Core REST API with search, filtering, paging, validation and CSV import. Re-importing updates products instead of duplicating them. |
| **Unit normalization** | `"180 m3/h"` → 50 l/s, `"0,18 kW"` → 180 W, `"Ø160"`/`"DN160"` → 160 mm. Finnish categories (`tuloilmalaite`, `puhallin`, `palopelti`, `valaisin`) are recognised. |
| **IFC model auditor** | Reads IFC4 models with [xBIM](https://github.com/xBimTeam/XbimEssentials), matches each device to the catalog via the standard `Pset_ManufacturerTypeInformation`, and classifies it as *OK*, *needs update*, *unidentified*, *not in catalog* or *wrong product type*. |
| **Auto-fix** | Writes catalog values back into the model and records which catalog product they came from, so the change is traceable. |
| **Excel report** | Summary, a filterable device list, a *designer actions* to-do sheet with a "Done" column, and every value change. |
| **AI datasheet reading** | Upload a manufacturer PDF; Claude reads the product table, the values go through the same normalizer as the CSV import, and a person reviews them before anything is saved. |
| **Web UI** | React + TypeScript + shadcn/ui: catalog management, drag-and-drop model audit, and downloads of the fixed model and report. |
| **CLI** | `MepCatalog.Auditor audit model.ifc --report audit.xlsx --fix fixed.ifc`, with a non-zero exit code while problems remain, so it can gate a pipeline. |

## Architecture

```mermaid
flowchart LR
    subgraph Clients
        WEB[React web app]
        CLI[Auditor CLI]
    end
    subgraph Api[ASP.NET Core API]
        P[/api/products/]
        A[/api/audits/]
        D[/api/datasheets/]
    end
    WEB --> P & A & D
    CLI -- REST --> P
    P --> DATA[(SQLite via EF Core)]
    A --> CORE[Core: DeviceAuditor<br/>ProductNormalizer]
    CLI --> CORE
    A --> IFC[Ifc adapter<br/>xBIM]
    A --> REP[Reporting<br/>ClosedXML]
    D --> AI[Ai: Claude datasheet extractor]
    AI --> CORE
```

| Project | Responsibility |
|---|---|
| `MepCatalog.Core` | Domain model and rules: product, unit parsing, normalization, **the audit logic**. No dependencies on files, databases or frameworks. |
| `MepCatalog.Data` | EF Core + SQLite, migrations, CSV reader, import/upsert service, catalog lookup. |
| `MepCatalog.Ifc` | Thin adapter between IFC4 (xBIM) and the core `ModelDevice` record, plus a sample model generator. |
| `MepCatalog.Reporting` | Excel and CSV audit reports. |
| `MepCatalog.Ai` | Claude-based PDF datasheet extraction (optional). |
| `MepCatalog.Api` | REST API with OpenAPI docs (Scalar UI at `/scalar`). |
| `MepCatalog.Auditor` / `MepCatalog.Importer` | Command-line tools. |
| `web/` | React 19 + TypeScript + Vite + Tailwind + shadcn/ui + TanStack Query. |

## Design decisions

- **The auditor doesn't know about IFC.** `DeviceAuditor` works on a format-neutral `ModelDevice` record. IFC is one adapter; a Revit add-in would be another adapter over the same, already-tested rules.
- **AI reads, code calculates.** Claude returns each value *exactly as printed* ("432 m3/h"), constrained by a JSON schema. Unit conversion runs through the same tested `ProductNormalizer` as the CSV import, so a language model never does arithmetic on engineering data.
- **A person approves AI output.** Extraction only proposes products. Each one shows the converted value next to the value as printed, problems are flagged, and nothing is saved until the user confirms.
- **Don't guess.** Devices with no product, an unknown product or the wrong kind of product are never "fixed" automatically. They go on the designer's to-do list instead.
- **Edit models safely.** A property set shared by several elements is never edited in place, so fixing one device can't silently change another.
- **The AI feature is optional.** Without an API key the app works normally and the feature explains how to enable it. Tests never call the paid API.

## Getting started

Prerequisites: [.NET 9 SDK](https://dotnet.microsoft.com/download) and [Node.js 22.12+](https://nodejs.org).

```bash
# 1. API (creates the SQLite database on first run) -> http://localhost:5236/scalar
dotnet run --project src/MepCatalog.Api

# 2. Load the sample catalog (in a second terminal)
dotnet run --project src/MepCatalog.Importer -- data/sample-products.csv --db src/MepCatalog.Api/mepcatalog.db

# 3. Web app -> http://localhost:5173
cd web
npm install
npm run dev
```

Then open **Model audit** and drop in `data/sample-building.ifc`.

### Command line

```bash
dotnet run --project src/MepCatalog.Auditor -- audit data/sample-building.ifc --report audit.xlsx --fix fixed.ifc
dotnet run --project src/MepCatalog.Auditor -- sample my-test-model.ifc
```

### Enabling AI datasheet reading (optional)

Create an API key in the [Anthropic Console](https://console.anthropic.com) and store it with .NET user-secrets, which keeps it outside the repository:

```bash
dotnet user-secrets set "Anthropic:ApiKey" "<your key>" --project src/MepCatalog.Api
```

Restart the API, then use **Catalog → From datasheet** with `data/datasheets/nordic-air-ka-series.pdf`. The `ANTHROPIC_API_KEY` environment variable also works.

## Tests and CI

```bash
dotnet test
```

61 tests cover unit parsing, normalization, the audit rules, an IFC round trip (create → audit → fix → save → reopen → audit again), the Excel report contents, and the HTTP API end to end against an in-memory database.

GitHub Actions builds the solution with warnings as errors, runs the tests on Linux, and type-checks, lints and builds the web app on every push.

## Limitations and next steps

- **Revit add-in**: the planned next adapter, reusing `DeviceAuditor` inside Revit through the Revit API.
- **Standard property sets**: technical values currently live in a `MepCatalog_ProductData` set; mapping to standard IFC sets such as `Pset_AirTerminalTypeCommon` is the next step.
- **IFC2x3**: only IFC4 is supported. IFC2x3 models represent these devices as generic `IfcFlowTerminal` / `IfcFlowController` elements with type objects, which needs its own adapter.
- **Authentication and hosting**: the API has no login yet. The natural production setup is Azure App Service + Azure SQL with Entra ID sign-in.
