"""Command line: `catalog-quality import <folder>` and `catalog-quality check`."""

from __future__ import annotations

import argparse
import os
import sys
from collections import Counter
from pathlib import Path

from catalog_quality.api import ApiError, CatalogClient
from catalog_quality.checks import Severity, run_checks
from catalog_quality.report import to_markdown

EXIT_OK, EXIT_FINDINGS, EXIT_FAILURE = 0, 1, 2

# --fail-on value -> which finding severities make the command exit with EXIT_FINDINGS.
FAILING_SEVERITIES: dict[str, set[Severity]] = {
    "error": {Severity.ERROR},
    "warning": {Severity.ERROR, Severity.WARNING},
    "never": set(),
}


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="catalog-quality", description=__doc__)
    parser.add_argument(
        "--api",
        default=os.environ.get("MEPCATALOG_API", "http://localhost:5236"),
        help="catalog API address (default: $MEPCATALOG_API or http://localhost:5236)",
    )
    commands = parser.add_subparsers(dest="command", required=True)

    importer = commands.add_parser("import", help="upload every manufacturer CSV in a folder")
    importer.add_argument("folder", type=Path)
    importer.add_argument("--pattern", default="*.csv", help="file name pattern (default: *.csv)")

    check = commands.add_parser("check", help="check the whole catalog for data-quality problems")
    check.add_argument("--report", type=Path, help="also write a Markdown report to this file")
    check.add_argument(
        "--fail-on",
        choices=["error", "warning", "never"],
        default="error",
        help="exit with code 1 when findings of this severity exist (default: error)",
    )
    return parser


def run_import(client: CatalogClient, folder: Path, pattern: str) -> int:
    files = sorted(folder.glob(pattern))
    if not files:
        print(f"No files matching {pattern} in {folder}", file=sys.stderr)
        return EXIT_FAILURE

    totals: Counter[str] = Counter()
    for path in files:
        result = client.import_csv(path)
        totals.update(created=result.created, updated=result.updated, failed=len(result.failed))
        counts = f"created {result.created:>3}  updated {result.updated:>3}  failed {len(result.failed):>3}"
        print(f"{result.file:<32} {counts}")
        for row in result.failed:
            print(f"    row {row['rowNumber']}: {'; '.join(row['errors'])}")

    summary = f"{totals['created']} created, {totals['updated']} updated, {totals['failed']} failed"
    print(f"\n{len(files)} files: {summary}")
    return EXIT_FINDINGS if totals["failed"] else EXIT_OK


def run_check(client: CatalogClient, report: Path | None, fail_on: str) -> int:
    products = list(client.iter_products())
    findings = run_checks(products)
    counts = Counter(f.severity for f in findings)

    for f in findings:
        print(f"{f.severity.value.upper():<8} {f.product:<32} {f.check:<20} {f.message}")
    print(f"\n{len(products)} products: {counts[Severity.ERROR]} errors, {counts[Severity.WARNING]} warnings")

    if report:
        report.write_text(to_markdown(products, findings, client.base_url), encoding="utf-8")
        print(f"Report written to {report}")

    failing = FAILING_SEVERITIES[fail_on]
    return EXIT_FINDINGS if any(f.severity in failing for f in findings) else EXIT_OK


def main(argv: list[str] | None = None, client: CatalogClient | None = None) -> int:
    args = build_parser().parse_args(argv)
    client = client or CatalogClient(args.api)
    try:
        if args.command == "import":
            return run_import(client, args.folder, args.pattern)
        return run_check(client, args.report, args.fail_on)
    except ApiError as error:
        print(error, file=sys.stderr)
        return EXIT_FAILURE


if __name__ == "__main__":
    sys.exit(main())
