"""Show or hide a local on-screen keyboard. No shell and no typed command.

Wayland on the Pi is usually squeekboard or wvkbd. Neither is installed by
this module. client/scripts/keyboard-packages.sh prints the apt line and does
not run during install.
"""

from __future__ import annotations

import os
import shutil
import signal
import subprocess
from pathlib import Path

ACTIONS = {"toggle", "show", "hide"}


def runtime_pid(directory: Path) -> int | None:
    path = directory / "keyboard.pid"
    try:
        text = path.read_text(encoding="utf-8").strip()
    except OSError:
        return None
    if not text.isdigit():
        return None
    return int(text)


def toggle(action: str, directory: Path) -> dict:
    if action not in ACTIONS:
        return {"ok": False, "error": "action", "detail": "That keyboard action is not available."}
    visible = _squeekboard_visible()
    if action == "toggle":
        action = "hide" if visible or _wvkbd_running(directory) else "show"
    if action == "hide":
        return _hide(directory, visible)
    return _show(directory)


def _show(directory: Path) -> dict:
    if shutil.which("busctl") and os.environ.get("WAYLAND_DISPLAY"):
        result = _busctl(True)
        if result["ok"]:
            return result
    binary = shutil.which("wvkbd-mobintl") or shutil.which("wvkbd")
    if not binary:
        return {
            "ok": False,
            "visible": False,
            "detail": "No on-screen keyboard is installed. On the Pi, sudo apt install wvkbd  — install is not run automatically.",
        }
    if _wvkbd_running(directory):
        return {"ok": True, "visible": True, "detail": "The on-screen keyboard is already open."}
    proc = subprocess.Popen([binary], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    directory.mkdir(parents=True, exist_ok=True)
    (directory / "keyboard.pid").write_text(str(proc.pid) + "\n", encoding="utf-8")
    return {"ok": True, "visible": True, "detail": "Opened the on-screen keyboard."}


def _hide(directory: Path, squeek_visible: bool) -> dict:
    if squeek_visible:
        result = _busctl(False)
        if result["ok"]:
            return result
    pid = runtime_pid(directory)
    if pid and _cmdline_is_wvkbd(pid):
        try:
            os.kill(pid, signal.SIGTERM)
        except OSError:
            pass
        try:
            (directory / "keyboard.pid").unlink()
        except OSError:
            pass
        return {"ok": True, "visible": False, "detail": "Closed the on-screen keyboard."}
    return {"ok": True, "visible": False, "detail": "The on-screen keyboard is not open."}


def _wvkbd_running(directory: Path) -> bool:
    pid = runtime_pid(directory)
    return bool(pid and _cmdline_is_wvkbd(pid))


def _cmdline_is_wvkbd(pid: int) -> bool:
    try:
        raw = Path(f"/proc/{pid}/cmdline").read_bytes()
    except OSError:
        return False
    text = raw.replace(b"\0", b" ").decode("utf-8", "replace")
    return "wvkbd" in text and "kiosk" not in text


def _squeekboard_visible() -> bool:
    if not shutil.which("busctl") or not os.environ.get("WAYLAND_DISPLAY"):
        return False
    proc = subprocess.run(
        ["busctl", "--user", "get-property", "sm.puri.OSK0", "/sm/puri/OSK0", "sm.puri.OSK0", "Visible"],
        capture_output=True,
        text=True,
        timeout=1,
        check=False,
    )
    return proc.returncode == 0 and proc.stdout.strip().endswith("true")


def _busctl(visible: bool) -> dict:
    proc = subprocess.run(
        ["busctl", "--user", "call", "sm.puri.OSK0", "/sm/puri/OSK0", "sm.puri.OSK0", "SetVisible", "b", "true" if visible else "false"],
        capture_output=True,
        text=True,
        timeout=2,
        check=False,
    )
    if proc.returncode != 0:
        return {"ok": False, "visible": False, "detail": "squeekboard did not answer."}
    return {"ok": True, "visible": visible, "detail": "On-screen keyboard updated."}
