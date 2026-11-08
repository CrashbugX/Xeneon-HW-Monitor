"""Local page server and host poller.

The browser only talks to 127.0.0.1. The Windows token stays in this process.
Polling the host is about 1 Hz. The page's requestAnimationFrame loop is
separate and keeps drawing if a poll is slow.
"""

from __future__ import annotations

import html
import json
import secrets
import threading
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

from xeneon.session import Session
from xeneon.wifi import read_wifi

STATIC = Path(__file__).resolve().parent / "static"


class Bridge:
    """Serves the dashboard and refreshes Session from the host or demo data."""

    def __init__(self, session: Session, host_url: str | None, token: str | None, port: int, setup=None, spotify=None) -> None:
        self.session = session
        self.host_url = host_url.rstrip("/") if host_url else None
        self.token = token or ""
        self.port = port
        self.setup = setup
        self.spotify = spotify
        self.csrf = secrets.token_urlsafe(24)
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

    def setup_view(self) -> dict:
        """Public setup state. Tokens and the Spotify refresh token are not included."""
        modules = {"cpu": True, "gpu": True, "load": True, "memory": True, "fans": True, "spotify": True}
        hosts = []
        client_id_set = False
        spotify_connected = False
        redirect = f"http://127.0.0.1:{self.port}/spotify/callback"
        if self.setup is not None:
            modules.update(self.setup.modules)
            hosts = self.setup.hosts
            client_id_set = bool(self.setup.client_id)
            redirect = self.setup.redirect_uri(self.port)
        if self.spotify is not None:
            spotify_connected = self.spotify.logged_in()
            client_id_set = not self.spotify.needs_client_id()
        return {
            "ok": True,
            "csrf": self.csrf,
            "paired": bool(self.token),
            "hostUrl": self.host_url or "",
            "hosts": hosts,
            "modules": modules,
            "spotifyClientId": (self.spotify.client_id if self.spotify is not None else (self.setup.client_id if self.setup is not None else "")),
            "spotifyClientIdSet": client_id_set,
            "spotifyConnected": spotify_connected,
            "spotifyRedirectUri": redirect,
            "demo": self.session.mode == "demo",
        }

    def allow_post(self, handler: BaseHTTPRequestHandler) -> bool:
        if not _origin_ok(self, handler):
            return False
        sent = handler.headers.get("X-Xeneon-Setup") or ""
        return secrets.compare_digest(sent, self.csrf)

    def route_post(self, path: str, body: dict) -> tuple[int, dict]:
        if path == "/api/demo/spotify":
            if self.session.mode != "demo":
                return 404, {"ok": False, "error": "not_demo"}
            status, reason = self.session.demo_transport(str(body.get("action")))
            return status, {"ok": status == 200, "error": None if status == 200 else reason}
        if path == "/api/spotify/action":
            if self.session.mode == "demo" or self.spotify is None:
                return 409, {"ok": False, "error": "spotify_not_connected"}
            status, reason = self.spotify.command(str(body.get("action")))
            if status == 200:
                self.session.set_live_spotify(self.spotify.poll())
            return status, {"ok": status == 200, "error": None if status == 200 else reason}
        if self.setup is None:
            return 404, {"ok": False, "error": "not_found"}
        return self.setup.handle(path, body, self)

    def finish_spotify(self, handler: BaseHTTPRequestHandler) -> None:
        if self.spotify is None:
            _html(handler, 404, "Spotify is not configured.")
            return
        query = urllib.parse.urlparse(handler.path).query
        params = urllib.parse.parse_qs(query)
        code = (params.get("code") or [""])[0]
        state = (params.get("state") or [""])[0]
        error = (params.get("error") or [""])[0]
        if error:
            _html(handler, 400, "Spotify did not grant access. You can close this page.")
            return
        try:
            self.spotify.finish(code, state)
        except ValueError as exc:
            _html(handler, 400, str(exc))
            return
        _html(handler, 200, "Spotify connected. You can close this page and return to the dashboard.")

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
            if self.spotify is not None and self.session.mode != "demo":
                self.session.set_live_spotify(self.spotify.poll())


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
            if path == "/api/setup":
                _json(self, 200, bridge.setup_view())
                return
            if path == "/spotify/callback":
                bridge.finish_spotify(self)
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
            if not bridge.allow_post(self):
                _json(self, 403, {"ok": False, "error": "csrf"})
                return
            length = int(self.headers.get("Content-Length", "0") or 0)
            try:
                body = json.loads(self.rfile.read(length).decode("utf-8") or "{}")
            except (UnicodeDecodeError, json.JSONDecodeError):
                _json(self, 400, {"ok": False, "error": "json"})
                return
            status, payload = bridge.route_post(path, body if isinstance(body, dict) else {})
            _json(self, status, payload)

    return Handler


def _origin_ok(bridge: Bridge, handler: BaseHTTPRequestHandler) -> bool:
    origin = handler.headers.get("Origin")
    if not origin:
        return True
    return origin == f"http://127.0.0.1:{bridge.port}" or origin == f"http://[::1]:{bridge.port}"


def _json(handler: BaseHTTPRequestHandler, status: int, payload: dict) -> None:
    data = json.dumps(payload).encode("utf-8")
    handler.send_response(status)
    handler.send_header("Content-Type", "application/json")
    handler.send_header("Cache-Control", "no-store")
    handler.send_header("Content-Length", str(len(data)))
    handler.end_headers()
    handler.wfile.write(data)


def _html(handler: BaseHTTPRequestHandler, status: int, message: str) -> None:
    data = f"<!DOCTYPE html><html><body><p>{html.escape(message)}</p></body></html>".encode("utf-8")
    handler.send_response(status)
    handler.send_header("Content-Type", "text/html; charset=utf-8")
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
