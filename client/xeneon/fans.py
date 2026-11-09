"""Fan names, roles, and observed speed range. Nothing here writes fan duty.

The observed minimum and maximum are samples this dashboard has seen. They are
not the fan's physical stop or full speed. A person can change those with Fan
Control; this program does not.
"""

from __future__ import annotations

ROLES = {"unknown", "cpu", "gpu", "case", "other"}


def catalog(document: dict | None) -> list[dict]:
    """Every Fan sensor in the host inventory. Roles are not guessed from the name."""
    if not isinstance(document, dict):
        return []
    inventory = document.get("inventory")
    if not isinstance(inventory, list):
        return []
    found = []
    for sensor in inventory:
        if not isinstance(sensor, dict) or sensor.get("sensorType") != "Fan":
            continue
        status = str(sensor.get("status") or "")
        rpm = sensor.get("normalizedValue")
        if status != "fresh" or isinstance(rpm, bool) or not isinstance(rpm, (int, float)):
            rpm = None
        found.append({
            "id": str(sensor.get("id") or ""),
            "name": str(sensor.get("name") or "Fan"),
            "hardwareName": str(sensor.get("hardwareName") or ""),
            "hardwareType": str(sensor.get("hardwareType") or ""),
            "rpm": None if rpm is None else float(rpm),
            "status": status or "missing",
        })
    return [item for item in found if item["id"]]


def default_pref(sensor_id: str) -> dict:
    return {
        "id": sensor_id,
        "label": "",
        "role": "unknown",
        "minRpm": None,
        "maxRpm": None,
        "minMode": "observed",
        "maxMode": "observed",
        "samples": 0,
    }


def observe(pref: dict, rpm: float | None) -> dict:
    """Fold one real sample into an observed range. Manual limits stay put."""
    if rpm is None:
        return pref
    samples = int(pref.get("samples") or 0) + 1
    pref["samples"] = samples
    if pref.get("minMode") != "manual":
        current = pref.get("minRpm")
        pref["minRpm"] = float(rpm) if current is None else min(float(current), float(rpm))
        pref["minMode"] = "observed"
    if pref.get("maxMode") != "manual":
        current = pref.get("maxRpm")
        pref["maxRpm"] = float(rpm) if current is None else max(float(current), float(rpm))
        pref["maxMode"] = "observed"
    return pref


def apply_edit(pref: dict, body: dict) -> dict:
    """User edit. op is save, reset, or clear. Unknown roles stay unknown."""
    op = str(body.get("op") or "save")
    if op == "clear":
        pref["minRpm"] = None
        pref["maxRpm"] = None
        pref["minMode"] = "observed"
        pref["maxMode"] = "observed"
        pref["samples"] = 0
        return pref
    if op == "reset":
        pref["minMode"] = "observed"
        pref["maxMode"] = "observed"
        pref["minRpm"] = None
        pref["maxRpm"] = None
        pref["samples"] = 0
        return pref
    label = body.get("label")
    if isinstance(label, str) and len(label) <= 40:
        pref["label"] = label.strip()
    role = str(body.get("role") or pref.get("role") or "unknown")
    if role in ROLES:
        pref["role"] = role
    if "minRpm" in body:
        pref["minRpm"] = _rpm(body.get("minRpm"))
        pref["minMode"] = "manual" if pref["minRpm"] is not None else "observed"
    if "maxRpm" in body:
        pref["maxRpm"] = _rpm(body.get("maxRpm"))
        pref["maxMode"] = "manual" if pref["maxRpm"] is not None else "observed"
    return pref


def merge(sensors: list[dict], prefs: dict) -> list[dict]:
    rows = []
    for sensor in sensors:
        pref = dict(default_pref(sensor["id"]))
        stored = prefs.get(sensor["id"])
        if isinstance(stored, dict):
            pref.update({key: stored.get(key, pref[key]) for key in pref})
            if pref.get("role") not in ROLES:
                pref["role"] = "unknown"
        pref["hardwareName"] = sensor["hardwareName"]
        pref["hardwareType"] = sensor["hardwareType"]
        pref["sensorName"] = sensor["name"]
        pref["rpm"] = sensor.get("rpm")
        pref["status"] = sensor["status"]
        rows.append(pref)
    return rows


def _rpm(value: object) -> float | None:
    if value is None or value == "":
        return None
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None
    if value < 0 or value > 20000:
        return None
    return float(value)
