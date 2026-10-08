import math

import pytest

from catalog_quality.api import Product
from catalog_quality.checks import (
    Severity,
    check_duplicates,
    check_plausibility,
    check_required_fields,
    duct_velocity,
    normalize_manufacturer,
    normalize_model,
    run_checks,
    specific_fan_power,
)


def product(category="SupplyAirTerminal", model="KA-160", manufacturer="Nordic Air Oy", **values) -> Product:
    defaults = {"airflow_lps": 50, "connection_size_mm": 160, "weight_kg": 1.6}
    if category == "Fan":
        defaults |= {"power_w": 310}
    if category == "LightFixture":
        defaults = {"power_w": 28, "weight_kg": 3.2}
    return Product(id=1, manufacturer=manufacturer, model=model, category=category, **(defaults | values))


def test_duct_velocity_of_a_typical_diffuser():
    # 50 l/s through a 160 mm neck: 0.05 / (pi * 0.08^2) = 2.49 m/s
    assert duct_velocity(50, 160) == pytest.approx(0.05 / (math.pi * 0.08**2))
    assert duct_velocity(50, 160) == pytest.approx(2.49, abs=0.01)


def test_specific_fan_power_is_watts_per_litre_per_second():
    assert specific_fan_power(310, 700) == pytest.approx(0.443, abs=0.001)


def test_complete_products_have_no_findings():
    assert check_required_fields(product()) == []
    assert check_plausibility(product()) == []
    assert check_plausibility(product("Fan", "VT-EC 315", airflow_lps=700, connection_size_mm=315)) == []


def test_missing_key_value_is_an_error_and_missing_weight_a_warning():
    findings = check_required_fields(product(airflow_lps=None, weight_kg=None))

    assert [(f.severity, f.message) for f in findings] == [
        (Severity.ERROR, "No airflow."),
        (Severity.WARNING, "No weight."),
    ]


def test_light_fixture_needs_power_not_airflow():
    findings = check_required_fields(product("LightFixture", "LX-1", power_w=None))

    assert [f.message for f in findings] == ["No power."]


@pytest.mark.parametrize(
    ("airflow", "size", "expected"),
    [
        (40, 160, None),  # 2.0 m/s: fine
        (110, 160, Severity.WARNING),  # 5.5 m/s is fine, 6+ is noisy...
        (140, 160, Severity.WARNING),  # 7.0 m/s: noisy
        (80, 100, Severity.ERROR),  # 10.2 m/s: probably a data error
    ],
)
def test_terminal_air_velocity(airflow, size, expected):
    findings = check_plausibility(product(airflow_lps=airflow, connection_size_mm=size))

    if expected is None or duct_velocity(airflow, size) <= 6:
        assert findings == []
    else:
        assert [f.severity for f in findings] == [expected]
        assert findings[0].check == "air-velocity"


def test_inefficient_fan_is_flagged():
    # 450 W for 150 l/s -> SFP 3.0 kW/(m³/s)
    findings = check_plausibility(
        product("Fan", "VT-AC 200", airflow_lps=150, power_w=450, connection_size_mm=200)
    )

    assert [f.check for f in findings] == ["fan-efficiency"]
    assert "3.00" in findings[0].message


@pytest.mark.parametrize(
    ("raw", "expected"),
    [("Nordic Air Oy", "nordicair"), ("NORDIC AIR", "nordicair"), ("Nordic-Air Ltd.", "nordicair")],
)
def test_manufacturer_names_are_normalized(raw, expected):
    assert normalize_manufacturer(raw) == expected


def test_model_codes_are_normalized():
    assert normalize_model("KA-125") == normalize_model("ka 125") == "KA125"


def test_spelling_variants_are_reported_as_possible_duplicates():
    findings = check_duplicates(
        [
            product(model="KA-160"),
            product(model="KA 160", manufacturer="Nordic Air"),
            product(model="KA-200"),
        ]
    )

    assert len(findings) == 2
    assert {f.product for f in findings} == {"Nordic Air Oy KA-160", "Nordic Air KA 160"}


def test_run_checks_sorts_errors_first():
    findings = run_checks([product(model="A", weight_kg=None), product(model="B", airflow_lps=None)])

    assert [f.severity for f in findings] == [Severity.ERROR, Severity.WARNING]
