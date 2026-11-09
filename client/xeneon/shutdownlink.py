"""Create the Pi shutdown token and give it to the paired PC.

The token is a local file, mode 0600. It is not the telemetry token and it is
not written into the dashboard page. The PC learns it from POST /v1/control/register
after pairing and again on later connections, so an already-paired Pi does not
pair a second time. The body has no address: the PC uses the TCP peer.
"""

from __future__ import annotations

import json
import secrets
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path


def pi_shutdown_enabled(config: dict) -> bool:
    """On unless client.json explicitly sets piShutdown to false."""
    if "piShutdown" in config:
        return bool(config["piShutdown"])
    return True


def ensure_token(path: Path) -> str:
    """Return the existing shutdown token, or create one. The file stays private."""
    if path.is_file():
        text = path.read_text(encoding="utf-8").strip()
        if text:
            return text.splitlines()[0].strip()
    token = secrets.token_hex(32)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(token + "\n", encoding="utf-8")
    try:
        path.chmod(0o600)
    except OSError:
        pass
    return token


def host_ip(url: str) -> str:
    """Host address from http://192.168.1.20:8765. Empty when it is not a URL."""
    parsed = urllib.parse.urlparse(url or "")
    return parsed.hostname or ""


def register_body(control_port: int, shutdown_token: str) -> dict:
    """What the Pi posts. The PC address is not included."""
    return {"controlPort": control_port, "shutdownToken": shutdown_token}


def announce(host_url: str, telemetry_token: str, control_port: int, shutdown_token: str) -> int:
    """Tell the paired PC this Pi's shutdown token. Network errors return 0. A 404 is an older host."""
    if not host_url or not telemetry_token or not shutdown_token:
        return 0
    body = json.dumps(register_body(control_port, shutdown_token)).encode("utf-8")
    request = urllib.request.Request(
        host_url.rstrip("/") + "/v1/control/register",
        data=body,
        headers={
            "Authorization": "Bearer " + telemetry_token,
            "Content-Type": "application/json",
        },
        method="POST",
    )
    try:
        with urllib.request.urlopen(request, timeout=2) as response:
            return response.status
    except urllib.error.HTTPError as exc:
        return exc.code
    except (OSError, urllib.error.URLError, TimeoutError):
        return 0
