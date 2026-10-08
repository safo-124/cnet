"""Data-quality rules for catalog products.

Every rule is a pure function from products to findings, so the rules can be tested without the API.
The thresholds are engineering rules of thumb for flagging data a person should look at, not design limits.
"""

from __future__ import annotations

import math
import re
from collections import defaultdict
from collections.abc import Iterable
from dataclasses import dataclass
from enum import StrEnum

from catalog_quality.api import Product


class Severity(StrEnum):
    ERROR = "error"
    WARNING = "warning"


@dataclass(frozen=True)
class Finding:
    severity: Severity
    product: str
    check: str
    message: str


# Values a designer needs to use the product, per category: (missing = error, missing = warning).
REQUIRED_FIELDS: dict[str, tuple[tuple[str, ...], tuple[str, ...]]] = {
    "SupplyAirTerminal": (("airflow_lps", "connection_size_mm"), ("weight_kg",)),
    "ExhaustAirTerminal": (("airflow_lps", "connection_size_mm"), ("weight_kg",)),
    "Fan": (("airflow_lps", "power_w"), ("connection_size_mm", "weight_kg")),
    "Damper": (("connection_size_mm",), ("weight_kg",)),
    "LightFixture": (("power_w",), ("weight_kg",)),
}

FIELD_LABELS = {
    "airflow_lps": "airflow",
    "power_w": "power",
    "connection_size_mm": "connection size",
    "weight_kg": "weight",
}

# Air velocity in a terminal's duct connection (neck). Above ~6 m/s a ceiling diffuser is usually noisy;
# above ~10 m/s the airflow or the size is more likely a data error than a real product.
TERMINAL_VELOCITY_WARNING = 6.0
TERMINAL_VELOCITY_ERROR = 10.0
# Duct fans: above ~15 m/s in the connection, check the data.
FAN_VELOCITY_WARNING = 15.0

# Specific fan power, kW/(m³/s). Finnish building regulations limit a whole mechanical supply and
# exhaust system to 2.0 kW/(m³/s), so one fan above that on its own deserves a look.
SFP_WARNING = 2.0

LIGHT_FIXTURE_POWER_WARNING_W = 200


def duct_velocity(airflow_lps: float, diameter_mm: float) -> float:
    """Mean air velocity in a round duct, m/s."""
    area_m2 = math.pi * (diameter_mm / 1000) ** 2 / 4
    return (airflow_lps / 1000) / area_m2


def specific_fan_power(power_w: float, airflow_lps: float) -> float:
    """SFP in kW/(m³/s). Conveniently, W per l/s is the same number."""
    return power_w / airflow_lps


def check_required_fields(product: Product) -> list[Finding]:
    errors, warnings = REQUIRED_FIELDS.get(product.category, ((), ()))
    findings = []
    for severity, fields in ((Severity.ERROR, errors), (Severity.WARNING, warnings)):
        for name in fields:
            if getattr(product, name) is None:
                findings.append(Finding(severity, product.label, "missing-data", f"No {FIELD_LABELS[name]}."))
    return findings


def check_plausibility(product: Product) -> list[Finding]:
    findings = []
    airflow, size, power = product.airflow_lps, product.connection_size_mm, product.power_w

    if airflow and size:
        velocity = duct_velocity(airflow, size)
        detail = f"{airflow:g} l/s through Ø{size} is {velocity:.1f} m/s"
        if product.category in ("SupplyAirTerminal", "ExhaustAirTerminal"):
            if velocity > TERMINAL_VELOCITY_ERROR:
                findings.append(
                    Finding(
                        Severity.ERROR,
                        product.label,
                        "air-velocity",
                        f"{detail}; airflow or size is probably wrong.",
                    )
                )
            elif velocity > TERMINAL_VELOCITY_WARNING:
                findings.append(
                    Finding(
                        Severity.WARNING,
                        product.label,
                        "air-velocity",
                        f"{detail}; likely noisy at the nominal airflow.",
                    )
                )
        elif product.category == "Fan" and velocity > FAN_VELOCITY_WARNING:
            findings.append(
                Finding(Severity.WARNING, product.label, "air-velocity", f"{detail}; check the data.")
            )

    if product.category == "Fan" and airflow and power:
        sfp = specific_fan_power(power, airflow)
        if sfp > SFP_WARNING:
            findings.append(
                Finding(
                    Severity.WARNING,
                    product.label,
                    "fan-efficiency",
                    f"SFP {sfp:.2f} kW/(m³/s) is above {SFP_WARNING:.1f}, "
                    "the limit for a whole ventilation system.",
                )
            )

    if product.category == "LightFixture" and power and power > LIGHT_FIXTURE_POWER_WARNING_W:
        findings.append(
            Finding(
                Severity.WARNING, product.label, "power", f"{power:g} W is unusually high for a luminaire."
            )
        )

    return findings


_COMPANY_SUFFIX = re.compile(r"\b(oy|oyj|ab|as|a/s|gmbh|ltd|limited|inc|llc)\b\.?", re.IGNORECASE)


def normalize_manufacturer(name: str) -> str:
    """'Nordic Air Oy', 'NORDIC AIR' and 'Nordic-Air' all become 'nordicair'."""
    return re.sub(r"[^a-z0-9]", "", _COMPANY_SUFFIX.sub("", name.lower()))


def normalize_model(model: str) -> str:
    """'KA-125', 'KA 125' and 'ka125' all become 'KA125'."""
    return re.sub(r"[^A-Z0-9]", "", model.upper())


def check_duplicates(products: Iterable[Product]) -> list[Finding]:
    groups: dict[tuple[str, str], list[Product]] = defaultdict(list)
    for p in products:
        groups[(normalize_manufacturer(p.manufacturer), normalize_model(p.model))].append(p)

    findings = []
    for same in groups.values():
        if len(same) > 1:
            labels = ", ".join(f"'{p.label}'" for p in same)
            for p in same:
                findings.append(
                    Finding(
                        Severity.WARNING,
                        p.label,
                        "possible-duplicate",
                        f"Looks like the same product as: {labels}.",
                    )
                )
    return findings


def run_checks(products: list[Product]) -> list[Finding]:
    findings = []
    for product in products:
        findings += check_required_fields(product)
        findings += check_plausibility(product)
    findings += check_duplicates(products)
    order = {Severity.ERROR: 0, Severity.WARNING: 1}
    return sorted(findings, key=lambda f: (order[f.severity], f.product, f.check))
