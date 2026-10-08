"""Small client for the MepCatalog REST API."""

from __future__ import annotations

from collections.abc import Iterator
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import requests


@dataclass(frozen=True)
class Product:
    """A catalog product, with values in the catalog's SI units."""

    id: int
    manufacturer: str
    model: str
    category: str
    description: str | None = None
    airflow_lps: float | None = None
    power_w: float | None = None
    connection_size_mm: int | None = None
    weight_kg: float | None = None

    @property
    def label(self) -> str:
        return f"{self.manufacturer} {self.model}"

    @classmethod
    def from_api(cls, data: dict[str, Any]) -> Product:
        return cls(
            id=data["id"],
            manufacturer=data["manufacturer"],
            model=data["model"],
            category=data["category"],
            description=data.get("description"),
            airflow_lps=data.get("airflowLps"),
            power_w=data.get("powerW"),
            connection_size_mm=data.get("connectionSizeMm"),
            weight_kg=data.get("weightKg"),
        )


@dataclass(frozen=True)
class ImportResult:
    file: str
    created: int
    updated: int
    failed: list[dict[str, Any]] = field(default_factory=list)


class ApiError(Exception):
    """The API could not be reached or answered with an error."""


class CatalogClient:
    def __init__(self, base_url: str, session: requests.Session | None = None, timeout: float = 60) -> None:
        self.base_url = base_url.rstrip("/")
        self.session = session or requests.Session()
        self.timeout = timeout

    def iter_products(self, page_size: int = 200) -> Iterator[Product]:
        """Yields every product, following the API's paging."""
        page = 1
        while True:
            data = self._get("/api/products", params={"page": page, "pageSize": page_size})
            for item in data["items"]:
                yield Product.from_api(item)
            if page * page_size >= data["totalCount"]:
                return
            page += 1

    def import_csv(self, path: Path) -> ImportResult:
        with path.open("rb") as file:
            response = self._send(
                "POST", "/api/products/import", files={"file": (path.name, file, "text/csv")}
            )
        data = response.json()
        return ImportResult(path.name, data["created"], data["updated"], data.get("failed", []))

    def _get(self, url: str, **kwargs: Any) -> Any:
        return self._send("GET", url, **kwargs).json()

    def _send(self, method: str, url: str, **kwargs: Any) -> requests.Response:
        try:
            response = self.session.request(method, self.base_url + url, timeout=self.timeout, **kwargs)
        except requests.RequestException as error:
            raise ApiError(f"Cannot reach the catalog API at {self.base_url}: {error}") from error
        if not response.ok:
            raise ApiError(f"{method} {url} failed with HTTP {response.status_code}: {response.text[:300]}")
        return response
