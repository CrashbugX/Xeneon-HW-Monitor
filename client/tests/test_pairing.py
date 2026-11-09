"""Pairing request errors and the host begin contract."""

from __future__ import annotations

import json
import socket
import threading
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from tempfile import TemporaryDirectory

from xeneon.discover import select_live_host
from xeneon.kioskctl import display_mode
from xeneon.setupflow import SetupFlow


class _Bridge:
    def __init__(self) -> None:
        self.token = ""
        self.host_url = ""
        self.session = type("S", (), {"modules": {}})()


class _Host(BaseHTTPRequestHandler):
    """Speaks the 0.2.0 begin JSON: ok, pairingId, and no approval code."""

    def do_POST(self) -> None:  # noqa: N802
        length = int(self.headers.get("Content-Length", "0") or 0)
        raw = self.rfile.read(length)
        self.server.seen.append((self.path, raw))  # type: ignore[attr-defined]
        if self.path == "/v1/pair/begin":
            body = json.dumps({"ok": True, "pairingId": "pid-1", "expiresAtUtc": "2026-11-09T00:00:00Z"}).encode()
            self._send(200, body)
            return
        if self.path == "/v1/pair/complete":
            body = json.dumps({"ok": False, "error": {"code": "awaiting_approval", "message": "Approve the request on the Windows host first."}}).encode()
            self._send(409, body)
            return
        self._send(404, b"{}")

    def _send(self, status: int, body: bytes) -> None:
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, fmt: str, *args) -> None:
        return


class PairingRequestTests(unittest.TestCase):
    def test_unreachable_host_is_not_labeled_pair_begin(self):
        with TemporaryDirectory() as tmp:
            root = Path(tmp)
            flow = SetupFlow(root / "client.json", root / "host.token")
            sock = socket.socket()
            sock.bind(("127.0.0.1", 0))
            port = sock.getsockname()[1]
            sock.close()
            status, payload = flow._pair({"baseUrl": f"http://127.0.0.1:{port}", "code": ""}, _Bridge())
            self.assertNotEqual(200, status)
            self.assertNotEqual("pair_begin", payload["detail"])
            self.assertIn("did not answer", payload["detail"])
            self.assertIn(f"http://127.0.0.1:{port}/v1/pair/begin", payload["detail"])
            self.assertIn("discovery", payload["detail"].lower())
            self.assertEqual("", flow._pairing_id)

    def test_request_uses_the_selected_host_and_the_host_begin_shape(self):
        server = ThreadingHTTPServer(("127.0.0.1", 0), _Host)
        server.seen = []  # type: ignore[attr-defined]
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        port = server.server_address[1]
        try:
            with TemporaryDirectory() as tmp:
                root = Path(tmp)
                flow = SetupFlow(root / "client.json", root / "host.token")
                status, payload = flow._pair(
                    {"baseUrl": f"http://127.0.0.1:{port}", "code": "", "address": "192.168.1.40"},
                    _Bridge(),
                )
                self.assertEqual(200, status)
                self.assertEqual("enter-code", payload["step"])
                self.assertIn("Approve", payload["detail"])
                self.assertNotIn("pair_begin", payload["detail"])
                path, raw = server.seen[0]  # type: ignore[attr-defined]
                self.assertEqual("/v1/pair/begin", path)
                self.assertNotIn("192.168.1.40", raw.decode())
                refused, again = flow._pair({"baseUrl": f"http://127.0.0.1:{port}", "code": "000000"}, _Bridge())
                self.assertNotEqual(200, refused)
                self.assertIn("Approve", again["detail"])
                self.assertNotEqual("pair_begin", again["detail"])
        finally:
            server.shutdown()

    def test_rediscover_uses_the_token_and_prefers_the_saved_name(self):
        found = [
            {"name": "OTHER", "baseUrl": "http://10.0.0.2:8765"},
            {"name": "SOULERPC", "baseUrl": "http://10.0.0.9:8765"},
        ]
        chosen = select_live_host(found, lambda base: base.endswith("10.0.0.9:8765"), "SOULERPC")
        self.assertEqual("http://10.0.0.9:8765", chosen["baseUrl"])
        self.assertIsNone(select_live_host(found, lambda base: False, "SOULERPC"))

    def test_fullscreen_request_does_not_relaunch(self):
        with TemporaryDirectory() as tmp:
            runtime = Path(tmp)
            result = display_mode("fullscreen", False, runtime)
            self.assertFalse(result["signaled"])
            self.assertFalse(result["pending"])
            self.assertIn("not relaunched", result["detail"])
            self.assertIn("page requested fullscreen", (runtime / "window.log").read_text(encoding="utf-8"))

    def test_empty_selection_does_not_use_the_manual_placeholder(self):
        with TemporaryDirectory() as tmp:
            root = Path(tmp)
            flow = SetupFlow(root / "client.json", root / "host.token")
            status, payload = flow._pair({"baseUrl": "", "code": ""}, _Bridge())
            self.assertEqual(400, status)
            self.assertIn("selected host", payload["detail"])
            self.assertIn("Find hosts", payload["detail"])


if __name__ == "__main__":
    unittest.main()
