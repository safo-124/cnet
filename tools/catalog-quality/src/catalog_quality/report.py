"""Markdown report of a catalog quality check."""

from __future__ import annotations

from collections import Counter
from datetime import UTC, datetime

from catalog_quality.api import Product
from catalog_quality.checks import Finding, Severity

CATEGORY_LABELS = {
    "SupplyAirTerminal": "Supply air terminals",
    "ExhaustAirTerminal": "Exhaust air terminals",
    "Fan": "Fans",
    "Damper": "Dampers",
    "LightFixture": "Light fixtures",
}


def to_markdown(
    products: list[Product], findings: list[Finding], source: str, now: datetime | None = None
) -> str:
    now = now or datetime.now(UTC)
    with_errors = {f.product for f in findings if f.severity is Severity.ERROR}
    counts = Counter(f.severity for f in findings)

    lines = [
        "# Catalog quality report",
        "",
        f"Catalog: {source}  ",
        f"Checked: {now:%Y-%m-%d %H:%M} UTC  ",
        f"Products: {len(products)}, errors: {counts[Severity.ERROR]}, warnings: {counts[Severity.WARNING]}",
        "",
        "## Usable products per category",
        "",
        "A product is usable when it has no errors (all key values present and plausible).",
        "",
        "| Category | Products | Usable | Share |",
        "|---|---:|---:|---:|",
    ]
    for category, label in CATEGORY_LABELS.items():
        in_category = [p for p in products if p.category == category]
        if not in_category:
            continue
        usable = sum(1 for p in in_category if p.label not in with_errors)
        lines.append(f"| {label} | {len(in_category)} | {usable} | {usable / len(in_category):.0%} |")

    lines += ["", "## Findings", ""]
    if not findings:
        lines.append("No problems found.")
    else:
        lines += ["| Severity | Product | Check | Details |", "|---|---|---|---|"]
        for f in findings:
            message = f.message.replace("|", "\\|")
            lines.append(f"| {f.severity.value} | {f.product} | {f.check} | {message} |")

    return "\n".join(lines) + "\n"
