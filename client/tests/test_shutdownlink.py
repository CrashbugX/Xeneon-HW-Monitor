"""Shutdown token exchange and the poweroff listener's host check."""

from __future__ import annotations

import json
import tempfile
import threading
import time
import unittest
import urllib.error
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

from xeneon.poweroff import PoweroffServer, PoweroffState, peer_allowed
from xeneon.shutdownlink import announce, ensure_token, host_ip, pi_shutdown_enabled, register_body


def _now() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%fZ")


class TokenTests(unittest.TestCase):
    def test_token_is_created_once_and_stays_private(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "shutdown.token"
            first = ensure_token(path)
            second = ensure_token(path)
            self.assertEqual(first, second)
            self.assertGreaterEqual(len(first), 32)
            self.assertEqual(0o600, path.stat().st_mode & 0o777)

    def test_default_is_on_and_an_explicit_false_is_off(self):
        self.assertTrue(pi_shutdown_enabled({}))
        self.assertTrue(pi_shutdown_enabled({"controlAllowLan": False}))
        self.assertFalse(pi_shutdown_enabled({"piShutdown": False}))

    def test_register_body_does_not_name_a_pc(self):
        body = register_body(8766, "abc")
        self.assertEqual({"controlPort": 8766, "shutdownToken": "abc"}, body)
        self.assertNotIn("host", body)
        self.assertEqual("192.168.1.20", host_ip("http://192.168.1.20:8765"))

    def test_announce_uses_the_register_route(self):
        seen = {}

        class Handler(urllib.request.BaseHandler):
            def http_open(self, request):
                seen["url"] = request.full_url
                seen["auth"] = request.get_header("Authorization")
                seen["body"] = json.loads(request.data.decode("utf-8"))
                return urllib.response.addinfourl(tempfile.TemporaryFile(), {"Content-Length": "0"}, request.full_url, 200)

        # urllib's handler API is awkward. Call announce against a tiny local server instead.
        del Handler

        from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

        class Server(ThreadingHTTPServer):
            def __init__(self):
                super().__init__(("127.0.0.1", 0), Request)
                self.body = {}
                self.auth = ""

        class Request(BaseHTTPRequestHandler):
            def log_message(self, fmt, *args):
                return

            def do_POST(self):
                length = int(self.headers.get("Content-Length", "0") or 0)
                self.server.body = json.loads(self.rfile.read(length).decode("utf-8"))
                self.server.auth = self.headers.get("Authorization", "")
                self.server.path = self.path
                data = b'{"ok":true}'
                self.send_response(200)
                self.send_header("Content-Length", str(len(data)))
                self.end_headers()
                self.wfile.write(data)

        server = Server()
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        port = server.server_address[1]
        try:
            status = announce(f"http://127.0.0.1:{port}", "telemetry-token", 8766, "shutdown-secret")
            self.assertEqual(200, status)
            self.assertEqual("/v1/control/register", server.path)
            self.assertEqual("Bearer telemetry-token", server.auth)
            self.assertEqual(8766, server.body["controlPort"])
            self.assertNotIn("host", server.body)
        finally:
            server.shutdown()


class ListenerTests(unittest.TestCase):
    def test_only_the_paired_host_can_power_off(self):
        calls = []
        state = PoweroffState(lambda: calls.append("off"))
        server = PoweroffServer("127.0.0.1", 0, "shutdown-token", state, False, restrict_host="10.0.0.8")
        server.start()
        port = server.httpd.server_address[1]
        try:
            self.assertEqual(403, _post(port, "shutdown-token", "n-other"))
            self.assertEqual([], calls)
            self.assertFalse(peer_allowed("192.168.1.9", "10.0.0.8"))
            self.assertTrue(peer_allowed("::ffff:10.0.0.8", "10.0.0.8"))
        finally:
            server.stop()

    def test_paired_loopback_host_powers_off_once(self):
        calls = []
        state = PoweroffState(lambda: calls.append("off"))
        server = PoweroffServer("127.0.0.1", 0, "shutdown-token", state, False, restrict_host="127.0.0.1")
        server.start()
        port = server.httpd.server_address[1]
        try:
            self.assertEqual(401, _post(port, "nope", "n1"))
            self.assertEqual(202, _post(port, "shutdown-token", "n2"))
            deadline = time.time() + 2
            while time.time() < deadline and state.status()["state"] != "halt-issued":
                time.sleep(0.02)
            self.assertEqual(["off"], calls)
            self.assertEqual(202, _post(port, "shutdown-token", "n3"))
            time.sleep(0.05)
            self.assertEqual(["off"], calls)
            server.enabled = False
            self.assertEqual(403, _post(port, "shutdown-token", "n4"))
        finally:
            server.stop()


def _post(port: int, token: str, nonce: str) -> int:
    data = json.dumps({"action": "poweroff", "nonce": nonce, "issuedAtUtc": _now()}).encode("utf-8")
    request = urllib.request.Request(f"http://127.0.0.1:{port}/v1/poweroff", data=data, method="POST")
    request.add_header("Content-Type", "application/json")
    request.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(request, timeout=2) as response:
            return response.status
    except urllib.error.HTTPError as exc:
        return exc.code


if __name__ == "__main__":
    unittest.main()
