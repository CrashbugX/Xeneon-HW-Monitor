"""Local page server and host poller.

The browser only talks to 127.0.0.1. The Windows token stays in this process.
Polling the host is about 1 Hz. The page's requestAnimationFrame loop is
separate and keeps drawing if a poll is slow.
"""

from __future__ import annotations

import json
import threading
import urllib.error
import urllib.request
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

from xeneon.session import Session
from xeneon.wifi import read_wifi

STATIC = Path(__file__).resolve().parent / "static"


class Bridge:
    """Serves the dashboard and refreshes Session from the host or demo data."""

    def __init__(self, session: Session, host_url: str | None, token: str | None, port: int) -> None:
        self.session = session
        self.host_url = host_url.rstrip("/") if host_url else None
        self.token = token or ""
        self.port = port
        self._stop = threading.Event()
        handler = _page_factory(self)
        self.httpd = ThreadingHTTPServer(("127.0.0.1", port), handler)
        self.page_thread = threading.Thread(target=self.httpd.serve_forever, name="xeneon-page", daemon=True)
        self.poll_thread = threading.Thread(target=self._poll, name="xeneon-poll", daemon=True)

    def start(self) -> None:
        """Bind localhost and start polling. Does not open a browser."""
        self.page_thread.start()
        self.poll_thread.start()

    def stop(self) -> None:
        """Stop threads. Does not power off the Pi."""
        self._stop.set()
        self.httpd.shutdown()
        self.httpd.server_close()

    def _poll(self) -> None:
        while not self._stop.wait(1.0):
            if self.session.mode == "demo":
                continue
            now = datetime.now(timezone.utc)
            if not self.host_url or not self.token:
                self.session.note_failure("unauthorized", now)
                continue
            try:
                request = urllib.request.Request(
                    self.host_url + "/v1/telemetry",
                    headers={"Authorization": "Bearer " + self.token},
                )
                with urllib.request.urlopen(request, timeout=2) as response:
                    payload = json.loads(response.read().decode("utf-8"))
            except urllib.error.HTTPError as exc:
                kind = "unauthorized" if exc.code == 401 else "invalid"
                self.session.note_failure(kind, now)
                continue
            except (OSError, urllib.error.URLError, json.JSONDecodeError, TimeoutError):
                self.session.note_failure("unreachable", now)
                continue
            if not isinstance(payload, dict) or payload.get("ok") is False:
                self.session.note_failure("invalid", now)
                continue
            self.session.note_document(payload, now)


def _page_factory(bridge: Bridge):
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, fmt: str, *args) -> None:
            return

        def do_GET(self) -> None:  # noqa: N802
            path = self.path.split("?", 1)[0]
            if path == "/api/view":
                wifi = {"status": "unavailable", "ssid": None, "bars": None, "detail": "demo"} if bridge.session.mode == "demo" else read_wifi()
                body = bridge.session.view(datetime.now(timezone.utc), wifi)
                _json(self, 200, body)
                return
            if path == "/health":
                _json(self, 200, {"ok": True})
                return
            if path in ("/", "/index.html"):
                _file(self, STATIC / "index.html", "text/html; charset=utf-8")
                return
            if path == "/app.css":
                _file(self, STATIC / "app.css", "text/css; charset=utf-8")
                return
            if path == "/app.js":
                _file(self, STATIC / "app.js", "text/javascript; charset=utf-8")
                return
            _json(self, 404, {"ok": False, "error": "not_found"})

        def do_POST(self) -> None:  # noqa: N802
            path = self.path.split("?", 1)[0]
            if path != "/api/demo/spotify":
                _json(self, 404, {"ok": False, "error": "not_found"})
                return
            length = int(self.headers.get("Content-Length", "0") or 0)
            try:
                body = json.loads(self.rfile.read(length).decode("utf-8") or "{}")
            except (UnicodeDecodeError, json.JSONDecodeError):
                _json(self, 400, {"ok": False, "error": "json"})
                return
            status, reason = bridge.session.demo_transport(str(body.get("action")))
            _json(self, status, {"ok": status == 200, "error": None if status == 200 else reason})

    return Handler


def _json(handler: BaseHTTPRequestHandler, status: int, payload: dict) -> None:
    data = json.dumps(payload).encode("utf-8")
    handler.send_response(status)
    handler.send_header("Content-Type", "application/json")
    handler.send_header("Cache-Control", "no-store")
    handler.send_header("Content-Length", str(len(data)))
    handler.end_headers()
    handler.wfile.write(data)


def _file(handler: BaseHTTPRequestHandler, path: Path, content_type: str) -> None:
    if not path.is_file():
        _json(handler, 404, {"ok": False, "error": "missing_asset"})
        return
    data = path.read_bytes()
    handler.send_response(200)
    handler.send_header("Content-Type", content_type)
    handler.send_header("Cache-Control", "no-store")
    handler.send_header("Content-Length", str(len(data)))
    handler.end_headers()
    handler.wfile.write(data)
