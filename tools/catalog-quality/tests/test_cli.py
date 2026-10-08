from dataclasses import dataclass, field
from typing import Any

import pytest

from catalog_quality.api import CatalogClient
from catalog_quality.cli import EXIT_FAILURE, EXIT_FINDINGS, EXIT_OK, main


def api_product(id: int, model: str, **values: Any) -> dict[str, Any]:
    return {
        "id": id,
        "manufacturer": "Nordic Air Oy",
        "model": model,
        "category": "SupplyAirTerminal",
        "airflowLps": 50,
        "powerW": None,
        "connectionSizeMm": 160,
        "weightKg": 1.6,
        **values,
    }


@dataclass
class FakeResponse:
    payload: Any
    status_code: int = 200
    text: str = ""

    @property
    def ok(self) -> bool:
        return self.status_code < 400

    def json(self) -> Any:
        return self.payload


@dataclass
class FakeSession:
    """Answers the two API calls the tool makes: paged product listing and CSV import."""

    products: list[dict[str, Any]] = field(default_factory=list)
    calls: list[tuple[str, str, dict[str, Any]]] = field(default_factory=list)

    def request(self, method: str, url: str, timeout: float, **kwargs: Any) -> FakeResponse:
        self.calls.append((method, url, kwargs))
        if url.endswith("/api/products") and method == "GET":
            page, size = kwargs["params"]["page"], kwargs["params"]["pageSize"]
            items = self.products[(page - 1) * size : page * size]
            return FakeResponse(
                {"items": items, "page": page, "pageSize": size, "totalCount": len(self.products)}
            )
        if url.endswith("/api/products/import"):
            name = kwargs["files"]["file"][0]
            failed = [{"rowNumber": 3, "errors": ["Unknown category 'X'."]}] if "bad" in name else []
            return FakeResponse({"created": 2, "updated": 1, "failed": failed})
        return FakeResponse({"title": "Not found"}, status_code=404, text="Not found")


def client_for(session: FakeSession) -> CatalogClient:
    return CatalogClient("http://catalog.test", session=session)  # type: ignore[arg-type]


def test_products_are_read_across_all_pages():
    session = FakeSession([api_product(i, f"KA-{i}") for i in range(5)])

    products = list(client_for(session).iter_products(page_size=2))

    assert [p.model for p in products] == ["KA-0", "KA-1", "KA-2", "KA-3", "KA-4"]
    assert len(session.calls) == 3


def test_check_passes_on_a_clean_catalog(tmp_path, capsys):
    session = FakeSession([api_product(1, "KA-160")])
    report = tmp_path / "report.md"

    code = main(["check", "--report", str(report)], client=client_for(session))

    assert code == EXIT_OK
    assert "1 products: 0 errors, 0 warnings" in capsys.readouterr().out
    assert "No problems found." in report.read_text(encoding="utf-8")


def test_check_fails_on_errors_and_lists_them_in_the_report(tmp_path):
    session = FakeSession([api_product(1, "KA-160"), api_product(2, "KA-X", airflowLps=None)])
    report = tmp_path / "report.md"

    code = main(["check", "--report", str(report)], client=client_for(session))

    assert code == EXIT_FINDINGS
    text = report.read_text(encoding="utf-8")
    assert "| error | Nordic Air Oy KA-X | missing-data | No airflow. |" in text
    assert "| Supply air terminals | 2 | 1 | 50% |" in text


@pytest.mark.parametrize(
    ("fail_on", "expected"), [("warning", EXIT_FINDINGS), ("error", EXIT_OK), ("never", EXIT_OK)]
)
def test_fail_on_controls_the_exit_code(fail_on, expected):
    session = FakeSession([api_product(1, "KA-160", weightKg=None)])  # only a warning

    assert main(["check", "--fail-on", fail_on], client=client_for(session)) == expected


def test_import_uploads_every_csv_and_reports_failed_rows(tmp_path, capsys):
    (tmp_path / "good.csv").write_text("Manufacturer;Model\n", encoding="utf-8")
    (tmp_path / "bad.csv").write_text("Manufacturer;Model\n", encoding="utf-8")
    (tmp_path / "notes.txt").write_text("ignored", encoding="utf-8")
    session = FakeSession()

    code = main(["import", str(tmp_path)], client=client_for(session))

    out = capsys.readouterr().out
    assert code == EXIT_FINDINGS
    assert len(session.calls) == 2
    assert "row 3: Unknown category 'X'." in out
    assert "2 files: 4 created, 2 updated, 1 failed" in out


def test_unreachable_api_is_a_clear_failure(capsys):
    code = main(["--api", "http://127.0.0.1:9", "check"])

    assert code == EXIT_FAILURE
    assert "Cannot reach the catalog API" in capsys.readouterr().err
