"""Read the Pi's own Wi-Fi. Missing tools become an explicit unavailable state.

This never invents an SSID. Demo mode does not call this module.
"""

from __future__ import annotations

import shutil
import subprocess


def read_wifi() -> dict:
    """Return ssid and bar count, or status unavailable.

    Tries nmcli, then iw. A missing command or a failure is not an exception
    for the page; the card shows unavailable.
    """
    if shutil.which("nmcli"):
        parsed = _nmcli()
        if parsed is not None:
            return parsed
    if shutil.which("iw"):
        parsed = _iw()
        if parsed is not None:
            return parsed
    return {"status": "unavailable", "ssid": None, "bars": None, "detail": "no wifi provider"}


def _nmcli() -> dict | None:
    try:
        completed = subprocess.run(
            ["nmcli", "-t", "-f", "ACTIVE,SSID,SIGNAL", "dev", "wifi"],
            check=False,
            capture_output=True,
            text=True,
            timeout=2,
        )
    except (OSError, subprocess.TimeoutExpired):
        return None
    for line in completed.stdout.splitlines():
        parts = line.split(":")
        if len(parts) >= 3 and parts[0] == "yes" and parts[1]:
            signal = _int(parts[2])
            return {
                "status": "ok",
                "ssid": parts[1],
                "bars": _bars(signal),
                "detail": f"signal {signal}" if signal is not None else "nmcli",
            }
    return {"status": "unavailable", "ssid": None, "bars": None, "detail": "wifi radio has no active network"}


def _iw() -> dict | None:
    try:
        completed = subprocess.run(
            ["iw", "dev"],
            check=False,
            capture_output=True,
            text=True,
            timeout=2,
        )
    except (OSError, subprocess.TimeoutExpired):
        return None
    ssid = None
    for line in completed.stdout.splitlines():
        stripped = line.strip()
        if stripped.startswith("ssid "):
            ssid = stripped[5:]
    if not ssid:
        return None
    return {"status": "ok", "ssid": ssid, "bars": None, "detail": "iw has no signal percent"}


def _bars(signal: int | None) -> int | None:
    if signal is None:
        return None
    if signal >= 80:
        return 4
    if signal >= 60:
        return 3
    if signal >= 40:
        return 2
    if signal >= 20:
        return 1
    return 0


def _int(text: str) -> int | None:
    try:
        return int(text)
    except ValueError:
        return None
