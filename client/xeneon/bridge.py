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

from xeneon.keyboard import toggle as keyboard_toggle
from xeneon.kioskctl import callback_page, display_mode, request_action, runtime_dir, window_state
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
        self._misses = 0
        self._stop = threading.Event()
        handler = _page_factory(self)
        self.httpd = ThreadingHTTPServer(("127.0.0.1", port), handler)
        # Port 0 asks the OS for a free port. Origin checks use the port that was bound.
        self.port = int(self.httpd.server_address[1])
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
            "theme": self.setup.theme if self.setup is not None else "night",
            "moduleOrder": self.setup.module_order if self.setup is not None else [],
            "spotifyAccount": self.spotify.account_name() if self.spotify is not None and self.spotify.logged_in() else "",
            "fans": self.setup.fan_rows(self.session.document()) if self.setup is not None else [],
            "demo": self.session.mode == "demo",
        }

    def allow_post(self, handler: BaseHTTPRequestHandler) -> bool:
        if not _origin_ok(self, handler):
            return False
        sent = handler.headers.get("X-Xeneon-Setup") or ""
        if len(sent) != len(self.csrf):
            return False
        return secrets.compare_digest(sent, self.csrf)

    def route_post(self, path: str, body: dict) -> tuple[int, dict]:
        if path == "/api/demo/spotify":
            if self.session.mode != "demo":
                return 404, {"ok": False, "error": "not_demo"}
            status, reason = self.session.demo_transport(str(body.get("action")))
            return status, {"ok": status == 200, "error": None if status == 200 else reason}
        if path == "/api/window":
            action = str(body.get("action") or "")
            if action in {"fullscreen", "windowed"}:
                result = display_mode(action, bool(body.get("needsFallback")))
                return (200 if result.get("ok") else 400), result
            result = request_action(action)
            return (200 if result.get("ok") else 400), result
        if path == "/api/keyboard":
            result = keyboard_toggle(str(body.get("action") or ""), runtime_dir())
            return (200 if result.get("ok") else 400), result
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
            _raw_html(handler, 404, callback_page(False))
            return
        query = urllib.parse.urlparse(handler.path).query
        params = urllib.parse.parse_qs(query)
        code = (params.get("code") or [""])[0]
        state = (params.get("state") or [""])[0]
        error = (params.get("error") or [""])[0]
        if error:
            _raw_html(handler, 400, callback_page(False))
            return
        try:
            self.spotify.finish(code, state)
        except ValueError:
            _raw_html(handler, 400, callback_page(False))
            return
        _raw_html(handler, 200, callback_page(True))

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
                if exc.code == 401:
                    self._rediscover()
                continue
            except (OSError, urllib.error.URLError, json.JSONDecodeError, TimeoutError):
                self.session.note_failure("unreachable", now)
                self._misses += 1
                if self._misses >= 3:
                    self._misses = 0
                    self._rediscover()
                continue
            if not isinstance(payload, dict) or payload.get("ok") is False:
                self.session.note_failure("invalid", now)
                continue
            self._misses = 0
            self.session.note_document(payload, now)
            if self.setup is not None:
                self.setup.observe_document(payload)
            if self.spotify is not None and self.session.mode != "demo":
                self.session.set_live_spotify(self.spotify.poll())

    def _rediscover(self) -> None:
        """Find the paired PC again after its address changes. The token is not replaced."""
        if not self.token or self.setup is None:
            return
        from xeneon.discover import discover, select_live_host
        found = discover(timeout=0.5)
        self.setup.hosts = found
        chosen = select_live_host(found, lambda base: _token_accepts(base, self.token), self.setup.paired_name)
        if chosen is not None:
            base = str(chosen.get("baseUrl") or "").rstrip("/")
            self.host_url = base
            self.setup.remember_host(base, str(chosen.get("name") or self.setup.paired_name))


def _token_accepts(base: str, token: str) -> bool:
    request = urllib.request.Request(base + "/v1/telemetry", headers={"Authorization": "Bearer " + token})
    try:
        with urllib.request.urlopen(request, timeout=2) as response:
            return response.status == 200
    except (OSError, urllib.error.URLError, TimeoutError):
        return False


def _page_factory(bridge: Bridge):
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, fmt: str, *args) -> None:
            return

        def do_GET(self) -> None:  # noqa: N802
            path = self.path.split("?", 1)[0]
            if path == "/api/view":
                wifi = {"status": "unavailable", "ssid": None, "bars": None, "detail": "demo"} if bridge.session.mode == "demo" else read_wifi()
                body = bridge.session.view(datetime.now(timezone.utc), wifi)
                if bridge.setup is not None:
                    body["fanBoard"] = bridge.setup.fan_rows(bridge.session.document())
                    body["theme"] = bridge.setup.theme
                    body["moduleOrder"] = bridge.setup.module_order
                _json(self, 200, body)
                return
            if path == "/health":
                _json(self, 200, {"ok": True})
                return
            if path == "/api/setup":
                _json(self, 200, bridge.setup_view())
                return
            if path == "/api/window":
                _json(self, 200, window_state())
                return
            if path == "/spotify/callback":
                bridge.finish_spotify(self)
                return
            if path in ("/", "/index.html"):
                # The saved theme is on the root element before first paint.
                # Setting it after the fetch fades every card through the night colors.
                theme = "night"
                if bridge.setup is not None and bridge.setup.theme in {"day", "night"}:
                    theme = bridge.setup.theme
                document = (STATIC / "index.html").read_text(encoding="utf-8")
                document = document.replace("<html lang=\"en\">", f"<html lang=\"en\" data-theme=\"{theme}\">", 1)
                _raw_html(self, 200, document)
                return
            if path == "/app.css":
                _file(self, STATIC / "app.css", "text/css; charset=utf-8")
                return
            if path == "/app.js":
                _file(self, STATIC / "app.js", "text/javascript; charset=utf-8")
                return
            if path == "/demo-art.png":
                _file(self, STATIC / "demo-art.png", "image/png")
                return
            if path == "/art":
                _art(self, bridge)
                return
            _json(self, 404, {"ok": False, "error": "not_found"})

        def do_POST(self) -> None:  # noqa: N802
            path = self.path.split("?", 1)[0]
            # Window actions are an enum for this browser only. A missing or
            # foreign Origin, or a Host that is not this loopback port, is refused.
            if path == "/api/window" and not _window_origin_ok(bridge, self):
                _json(self, 403, {"ok": False, "error": "origin"})
                return
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


def _local_origins(bridge: Bridge) -> set[str]:
    return {f"http://127.0.0.1:{bridge.port}", f"http://[::1]:{bridge.port}"}


def _local_hosts(bridge: Bridge) -> set[str]:
    return {f"127.0.0.1:{bridge.port}", f"localhost:{bridge.port}", f"[::1]:{bridge.port}"}


def _origin_ok(bridge: Bridge, handler: BaseHTTPRequestHandler) -> bool:
    origin = handler.headers.get("Origin")
    if not origin:
        return True
    return origin in _local_origins(bridge)


def _window_origin_ok(bridge: Bridge, handler: BaseHTTPRequestHandler) -> bool:
    """Window controls require both a loopback Origin and a loopback Host.

    Other setup posts still allow a missing Origin so same-machine tools keep
    working. A page on the LAN cannot satisfy this check.
    """
    origin = handler.headers.get("Origin") or ""
    host = handler.headers.get("Host") or ""
    return origin in _local_origins(bridge) and host in _local_hosts(bridge)


def _json(handler: BaseHTTPRequestHandler, status: int, payload: dict) -> None:
    data = json.dumps(payload).encode("utf-8")
    handler.send_response(status)
    handler.send_header("Content-Type", "application/json")
    handler.send_header("Cache-Control", "no-store")
    handler.send_header("Content-Length", str(len(data)))
    handler.end_headers()
    handler.wfile.write(data)


def _raw_html(handler: BaseHTTPRequestHandler, status: int, document: str) -> None:
    data = document.encode("utf-8")
    handler.send_response(status)
    handler.send_header("Content-Type", "text/html; charset=utf-8")
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


def _art(handler: BaseHTTPRequestHandler, bridge: Bridge) -> None:
    """Same-origin album art so the page can sample a tint. Only Spotify image hosts."""
    from xeneon.spotify import ART_PREFIXES
    query = urllib.parse.urlparse(handler.path).query
    target = urllib.parse.parse_qs(query).get("u", [""])[0]
    if not any(target.startswith(prefix) for prefix in ART_PREFIXES):
        _json(handler, 400, {"ok": False, "error": "art"})
        return
    try:
        with urllib.request.urlopen(target, timeout=2) as response:
            data = response.read(1_500_000)
            kind = response.headers.get("Content-Type") or "image/jpeg"
    except (OSError, urllib.error.URLError, TimeoutError):
        _json(handler, 502, {"ok": False, "error": "art"})
        return
    handler.send_response(200)
    handler.send_header("Content-Type", kind.split(";")[0])
    handler.send_header("Cache-Control", "no-store")
    handler.send_header("Content-Length", str(len(data)))
    handler.end_headers()
    handler.wfile.write(data)
    _ = bridge


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
