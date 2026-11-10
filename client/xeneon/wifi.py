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


def split_nmcli(line: str) -> list[str]:
    """Split one `nmcli -t` line. Colons inside a field are escaped as \\:."""
    parts: list[str] = []
    current: list[str] = []
    escaped = False
    for char in line.rstrip("\n"):
        if escaped:
            current.append(char)
            escaped = False
        elif char == "\\":
            escaped = True
        elif char == ":":
            parts.append("".join(current))
            current = []
        else:
            current.append(char)
    parts.append("".join(current))
    return parts


def parse_wifi_table(text: str) -> list[dict]:
    """Rows from `nmcli -t -f ACTIVE,SSID,SIGNAL,SECURITY dev wifi`. Empty SSID rows are skipped."""
    found = []
    seen = set()
    for line in text.splitlines():
        parts = split_nmcli(line)
        if len(parts) < 3:
            continue
        ssid = parts[1].strip()
        if not ssid or ssid in seen:
            continue
        seen.add(ssid)
        signal = _int(parts[2])
        security = parts[3].strip() if len(parts) > 3 else ""
        found.append({
            "ssid": ssid,
            "active": parts[0].strip() == "yes",
            "signal": signal,
            "bars": _bars(signal),
            "security": security,
            "open": security == "",
        })
    return found


def parse_saved_connections(text: str) -> list[str]:
    """Wi-Fi connection names from `nmcli -t -f NAME,TYPE connection show`."""
    names = []
    for line in text.splitlines():
        parts = split_nmcli(line)
        if len(parts) >= 2 and "wireless" in parts[1]:
            name = parts[0].strip()
            if name and name not in names:
                names.append(name)
    return names


def parse_ipv4(text: str) -> str | None:
    """First dotted address in an `nmcli -g IP4.ADDRESS` blob. The prefix length is dropped."""
    for token in text.replace("\n", " ").split():
        address = token.split("/", 1)[0]
        pieces = address.split(".")
        if len(pieces) == 4 and all(piece.isdigit() and 0 <= int(piece) <= 255 for piece in pieces):
            return address
    return None


def plan_wifi_action(action: str, ssid: str, confirm: bool, password: str, saved: list[str], visible: list[str]) -> dict:
    """Decide the nmcli argv. This does not run it.

    Forget does nothing until confirm is true. A password is only added for a
    network that is actually in the scan or already saved. The password is not
    copied into the error.
    """
    name = ssid.strip()
    if action not in {"reconnect", "forget", "connect"}:
        return {"error": "action"}
    if not name or len(name) > 64 or any(char in name for char in "\r\n\x00"):
        return {"error": "ssid"}
    if action == "forget":
        if not confirm:
            return {"error": "confirm_required"}
        if name not in saved:
            return {"error": "not_saved"}
        return {"argv": ["nmcli", "connection", "delete", name]}
    if action == "reconnect":
        if name in saved:
            return {"argv": ["nmcli", "connection", "up", name]}
        if name not in visible:
            return {"error": "unknown_network"}
        return {"argv": ["nmcli", "device", "wifi", "connect", name]}
    if name not in saved and name not in visible:
        return {"error": "unknown_network"}
    if name in saved and not password:
        return {"argv": ["nmcli", "connection", "up", name]}
    if password:
        if len(password) > 128 or any(char in password for char in "\r\n\x00"):
            return {"error": "password"}
        return {"argv": ["nmcli", "device", "wifi", "connect", name, "password", password]}
    return {"argv": ["nmcli", "device", "wifi", "connect", name]}


def demo_wifi_detail() -> dict:
    """Sample networks for demo mode. Not a real scan, and not a personal SSID."""
    return {
        "ok": True,
        "available": True,
        "demo": True,
        "ssid": "Home Wi-Fi (example)",
        "bars": 3,
        "signal": 70,
        "status": "connected",
        "ip": "192.168.1.20",
        "security": "WPA2",
        "networks": [
            {"ssid": "Home Wi-Fi (example)", "active": True, "bars": 3, "signal": 70, "security": "WPA2", "open": False, "saved": True},
            {"ssid": "Guest", "active": False, "bars": 2, "signal": 45, "security": "", "open": True, "saved": False},
            {"ssid": "Lab", "active": False, "bars": 4, "signal": 88, "security": "WPA2", "open": False, "saved": True},
        ],
        "note": "Demo list. Nothing is changed on a real radio.",
    }


def live_wifi_detail(runner=None) -> dict:
    """Read NetworkManager. A missing nmcli is a clean unavailable card, not a crash."""
    import shutil
    if shutil.which("nmcli") is None and runner is None:
        return {
            "ok": True,
            "available": False,
            "demo": False,
            "ssid": None,
            "bars": None,
            "status": "unavailable",
            "ip": None,
            "networks": [],
            "note": "NetworkManager (nmcli) is not installed. Reconnect and Forget stay off.",
        }
    run = runner or _run

    def grab(argv: list[str]) -> str:
        return run(argv)

    networks = parse_wifi_table(grab(["nmcli", "-t", "-f", "ACTIVE,SSID,SIGNAL,SECURITY", "dev", "wifi"]))
    saved = parse_saved_connections(grab(["nmcli", "-t", "-f", "NAME,TYPE", "connection", "show"]))
    ip = parse_ipv4(grab(["nmcli", "-g", "IP4.ADDRESS", "device", "show"]))
    active = next((row for row in networks if row["active"]), None)
    merged = []
    for row in networks:
        item = dict(row)
        item["saved"] = row["ssid"] in saved
        merged.append(item)
    return {
        "ok": True,
        "available": True,
        "demo": False,
        "ssid": None if active is None else active["ssid"],
        "bars": None if active is None else active["bars"],
        "signal": None if active is None else active["signal"],
        "status": "connected" if active else "disconnected",
        "ip": ip,
        "security": "" if active is None else active["security"],
        "networks": merged,
        "note": "Saved networks connect without a password. A new locked network asks once. The password is not stored by this dashboard.",
    }


def run_wifi_action(action: str, ssid: str, confirm: bool, password: str, runner=None) -> dict:
    """Plan the command from a fresh scan, then run it. Demo callers should not use this."""
    import shutil
    if shutil.which("nmcli") is None and runner is None:
        return {"ok": False, "error": "nmcli_missing", "detail": "NetworkManager (nmcli) is not installed."}
    run = runner or _run
    detail = live_wifi_detail(run)
    saved = [row["ssid"] for row in detail["networks"] if row.get("saved")]
    # Saved names that are not currently visible still count.
    extra = parse_saved_connections(run(["nmcli", "-t", "-f", "NAME,TYPE", "connection", "show"]))
    for name in extra:
        if name not in saved:
            saved.append(name)
    visible = [row["ssid"] for row in detail["networks"]]
    plan = plan_wifi_action(action, ssid, confirm, password, saved, visible)
    if plan.get("error"):
        return {"ok": False, "error": plan["error"]}
    text = run(plan["argv"])
    return {"ok": True, "detail": text.strip()[:180]}


def _run(argv: list[str]) -> str:
    try:
        completed = subprocess.run(argv, check=False, capture_output=True, text=True, timeout=20)
    except (OSError, subprocess.TimeoutExpired) as exc:
        return str(exc)
    return (completed.stdout or "") + (completed.stderr or "")
