"""Authenticated poweroff route. The only action is poweroff.

Telemetry loss never reaches this module. A replayed nonce does not call the
helper again. The helper is a fixed argv, not a shell string.
"""

from __future__ import annotations

import hmac
import json
import threading
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

from xeneon.contract import parse_time


class PoweroffState:
    """Process-local shutdown progress. helper_calls counts real helper starts."""

    def __init__(self, helper) -> None:
        self.helper = helper
        self.lock = threading.Lock()
        self.phase = "idle"
        self.helper_calls = 0
        self.nonces: set[str] = set()

    def request(self, body: dict, now: datetime) -> tuple[int, dict]:
        """Validate one POST body. Returns HTTP status and a JSON object."""
        action = body.get("action")
        if action != "poweroff":
            return 403, {"ok": False, "error": "action_not_allowed"}
        nonce = body.get("nonce")
        if not isinstance(nonce, str) or not nonce:
            return 400, {"ok": False, "error": "nonce_required"}
        try:
            issued = parse_time(str(body.get("issuedAtUtc")))
        except (TypeError, ValueError):
            return 400, {"ok": False, "error": "issued_at"}
        if abs(now - issued) > timedelta(seconds=180):
            return 401, {"ok": False, "error": "clock_skew"}
        with self.lock:
            if nonce in self.nonces:
                return 409, {"ok": False, "error": "replay", "state": self.phase}
            self.nonces.add(nonce)
            if self.phase in ("accepted", "syncing", "halt-issued", "helper-missing"):
                return 202, {"ok": True, "state": self.phase, "repeated": True}
            self.phase = "accepted"
        self._start_helper()
        return 202, {"ok": True, "state": "accepted"}

    def status(self) -> dict:
        """Return the phase. Does not start the helper."""
        with self.lock:
            return {"ok": True, "state": self.phase, "helperCalls": self.helper_calls}

    def _start_helper(self) -> None:
        def run() -> None:
            with self.lock:
                self.phase = "syncing"
                self.helper_calls += 1
            try:
                self.helper()
            except Exception:
                with self.lock:
                    self.phase = "helper-missing"
                return
            with self.lock:
                if self.phase == "syncing":
                    self.phase = "halt-issued"

        threading.Thread(target=run, name="xeneon-poweroff", daemon=True).start()


def normalize_ip(value: str) -> str:
    """Compare IPv4 and IPv4-mapped IPv6 as the same address."""
    text = (value or "").strip()
    if text.startswith("::ffff:"):
        return text[7:]
    return text


def peer_allowed(peer: str, host_ip: str) -> bool:
    """True only when the request comes from the paired PC. An unknown PC is refused."""
    expected = normalize_ip(host_ip)
    if not expected:
        return False
    return normalize_ip(peer) == expected


class PoweroffServer:
    """LAN control listener. Bind-all is refused unless allow_lan is true.

    restrict_host is the paired PC address. None keeps older tests unrestricted.
    An empty string refuses every poweroff until a host is known.
    enabled is the Setup toggle. Turning it off does not uninstall the helper.
    """

    def __init__(self, bind: str, port: int, token: str, state: PoweroffState, allow_lan: bool, restrict_host: str | None = None) -> None:
        if bind in ("0.0.0.0", "::", "") and not allow_lan:
            raise ValueError("refusing control bind on all interfaces without allowLan")
        self.token = token
        self.state = state
        self.restrict_host = restrict_host
        self.enabled = True
        handler = _handler_factory(self)
        self.httpd = ThreadingHTTPServer((bind, port), handler)
        self.thread = threading.Thread(target=self.httpd.serve_forever, name="xeneon-control", daemon=True)

    def start(self) -> None:
        """Begin accepting control requests. Does not power off by itself."""
        self.thread.start()

    def stop(self) -> None:
        """Stop the listener. Does not power off the machine."""
        self.httpd.shutdown()
        self.httpd.server_close()


def _handler_factory(server: PoweroffServer):
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, fmt: str, *args) -> None:
            return

        def do_POST(self) -> None:  # noqa: N802 - BaseHTTPRequestHandler name
            if self.path.split("?", 1)[0] != "/v1/poweroff":
                _send(self, 404, {"ok": False, "error": "not_found"})
                return
            if not server.enabled:
                _send(self, 403, {"ok": False, "error": "poweroff_disabled"})
                return
            if not _authorized(self, server.token):
                _send(self, 401, {"ok": False, "error": "unauthorized"})
                return
            if not _peer_ok(self, server):
                _send(self, 403, {"ok": False, "error": "wrong_host"})
                return
            length = int(self.headers.get("Content-Length", "0") or 0)
            raw = self.rfile.read(length) if length else b""
            try:
                body = json.loads(raw.decode("utf-8"))
            except (UnicodeDecodeError, json.JSONDecodeError):
                _send(self, 400, {"ok": False, "error": "json"})
                return
            status, payload = server.state.request(body, datetime.now(timezone.utc))
            _send(self, status, payload)

        def do_GET(self) -> None:  # noqa: N802
            if self.path.split("?", 1)[0] != "/v1/poweroff/status":
                _send(self, 404, {"ok": False, "error": "not_found"})
                return
            if not server.enabled:
                _send(self, 403, {"ok": False, "error": "poweroff_disabled"})
                return
            if not _authorized(self, server.token):
                _send(self, 401, {"ok": False, "error": "unauthorized"})
                return
            if not _peer_ok(self, server):
                _send(self, 403, {"ok": False, "error": "wrong_host"})
                return
            # Status does not start the helper. The host reads it while it waits.
            _send(self, 200, server.state.status())

    return Handler


def _peer_ok(handler: BaseHTTPRequestHandler, server: PoweroffServer) -> bool:
    if server.restrict_host is None:
        return True
    peer = handler.client_address[0] if handler.client_address else ""
    return peer_allowed(peer, server.restrict_host)


def _authorized(handler: BaseHTTPRequestHandler, token: str) -> bool:
    header = handler.headers.get("Authorization", "")
    provided = header[7:] if header.startswith("Bearer ") else ""
    return hmac.compare_digest(provided, token)


def _send(handler: BaseHTTPRequestHandler, status: int, payload: dict) -> None:
    data = json.dumps(payload).encode("utf-8")
    handler.send_response(status)
    handler.send_header("Content-Type", "application/json")
    handler.send_header("Cache-Control", "no-store")
    handler.send_header("Content-Length", str(len(data)))
    handler.end_headers()
    handler.wfile.write(data)
