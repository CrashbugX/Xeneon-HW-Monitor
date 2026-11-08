"""Checks a host telemetry document before any number is shown.

The document is protocol/schemas/telemetry-snapshot-1.0.0.schema.json.
A status other than ok must not carry a number. A backwards sequence is
kept only when the timestamp shows the host process restarted.
"""

from __future__ import annotations

from datetime import datetime, timezone


def parse_time(value: str) -> datetime:
    """Parse an ISO-8601 UTC timestamp. Raises ValueError when it is not."""
    text = value.replace("Z", "+00:00")
    parsed = datetime.fromisoformat(text)
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=timezone.utc)
    return parsed.astimezone(timezone.utc)


def validate_telemetry(doc: object) -> list[str]:
    """Return a list of problems. An empty list means the document may be shown.

    doc is the JSON object from GET /v1/telemetry. This does not contact the host.
    """
    errors: list[str] = []
    if not isinstance(doc, dict):
        return ["document is not an object"]
    if doc.get("schemaVersion") != "1.0.0":
        errors.append("schemaVersion")
    if not isinstance(doc.get("synthetic"), bool):
        errors.append("synthetic")
    if not isinstance(doc.get("sampledOnce"), bool):
        errors.append("sampledOnce")
    try:
        parse_time(str(doc.get("sampledAtUtc")))
    except (TypeError, ValueError):
        errors.append("sampledAtUtc")
    readings = doc.get("readings")
    if not isinstance(readings, dict):
        errors.append("readings")
        return errors
    _reading(errors, _dig(readings, "cpu", "temperature"), "cpu.temperature")
    _reading(errors, _dig(readings, "cpu", "utilization", "total"), "cpu.utilization.total")
    logical = _dig(readings, "cpu", "utilization", "logical")
    if isinstance(logical, dict):
        for index, item in enumerate(logical.get("items") or []):
            _reading(errors, item, f"cpu.utilization.logical[{index}]")
    _reading(errors, _dig(readings, "gpu", "temperature"), "gpu.temperature")
    _reading(errors, _dig(readings, "gpu", "load"), "gpu.load")
    _reading(errors, _dig(readings, "gpu", "vram", "used"), "vram.used")
    _reading(errors, _dig(readings, "gpu", "vram", "total"), "vram.total")
    _reading(errors, _dig(readings, "memory", "used"), "memory.used")
    _reading(errors, _dig(readings, "memory", "total"), "memory.total")
    fans = readings.get("fans")
    if isinstance(fans, dict):
        for group in ("cpu", "gpu", "case"):
            block = fans.get(group)
            if isinstance(block, dict):
                for index, item in enumerate(block.get("items") or []):
                    _reading(errors, item, f"fans.{group}[{index}]")
    return errors


def accept_sequence(previous: int | None, previous_at: datetime | None, doc: dict) -> tuple[bool, str]:
    """Decide whether a newer document replaces the one on screen.

    Returns (accept, reason). A lower sequence with an older timestamp is
    replay or reordering and must not move the numbers backward. A lower
    sequence with a newer timestamp is a host restart and is accepted.
    """
    sequence = doc.get("sequence")
    sampled = parse_time(str(doc["sampledAtUtc"]))
    if sequence is None or previous is None:
        return True, "first"
    if not isinstance(sequence, int):
        return False, "sequence"
    if sequence < previous and previous_at is not None and sampled <= previous_at:
        return False, "out_of_order"
    return True, "in_order"


def reading_value(reading: object) -> float | None:
    """Return the number only when status is ok. Otherwise None, never zero-filled."""
    if not isinstance(reading, dict):
        return None
    if reading.get("status") != "ok":
        return None
    value = reading.get("value")
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None
    return float(value)


def _dig(node: dict, *keys: str) -> object:
    current: object = node
    for key in keys:
        if not isinstance(current, dict):
            return None
        current = current.get(key)
    return current


def _reading(errors: list[str], reading: object, path: str) -> None:
    if reading is None:
        return
    if not isinstance(reading, dict):
        errors.append(path)
        return
    status = reading.get("status")
    value = reading.get("value")
    if status != "ok" and value is not None:
        errors.append(f"{path} has a number while status is {status}")
    if status == "ok" and (isinstance(value, bool) or not isinstance(value, (int, float))):
        errors.append(f"{path} is ok without a number")
