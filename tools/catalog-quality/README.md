# catalog-quality

A Python command-line tool that works with the MepCatalog REST API:

- **`import <folder>`**: uploads every manufacturer CSV in a folder and summarizes what was created, updated or rejected.
- **`check`**: checks the whole catalog for data a designer couldn't rely on, and writes a Markdown report.

It uses only the public REST API, the same one the web app uses, so it works against a local API or the live demo.

## Install

```bash
cd tools/catalog-quality
python -m venv .venv
.venv/Scripts/activate        # Windows; on Linux/macOS: source .venv/bin/activate
pip install -e ".[dev]"
```

## Use

```bash
# Import a batch of manufacturer files (the API must be running)
catalog-quality import ../../data/import-batch

# Check the catalog and write a report
catalog-quality check --report quality-report.md

# Against the live demo (read-only check)
catalog-quality --api https://mepcatalog.135.181.93.156.nip.io check
```

`check` exits with code 1 when it finds errors (`--fail-on warning` or `--fail-on never` change that), so it can
gate a pipeline. Code 2 means the API couldn't be reached.

## What is checked

| Check | Severity | Rule |
|---|---|---|
| `missing-data` | error / warning | Values a designer needs per category. Air terminals need airflow and connection size, fans airflow and power, dampers a connection size, luminaires power. A missing weight is a warning. |
| `air-velocity` | warning / error | Mean velocity in the duct connection, *airflow ÷ (π·d²/4)*. Air terminals above 6 m/s are likely noisy; above 10 m/s the data is probably wrong. Fans above 15 m/s. |
| `fan-efficiency` | warning | Specific fan power, *P ÷ q*, in kW/(m³/s). Finnish building regulations limit a whole ventilation system to 2.0, so a single fan above it deserves a look. |
| `power` | warning | Luminaires above 200 W. |
| `possible-duplicate` | warning | Same product under spelling variants: `KA-125` / `KA 125`, `Nordic Air Oy` / `NORDIC AIR`. |

The thresholds are rules of thumb for flagging data to review, not design limits.

## Develop

```bash
pytest
ruff check .
```

The checks are pure functions (`checks.py`) and the CLI tests use a fake HTTP session, so the tests need no server.
