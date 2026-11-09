"""Client tests. They never power off the machine or edit boot files."""

from __future__ import annotations

import json
import os
import subprocess
import time
import unittest
import urllib.error
import urllib.request
from datetime import datetime, timedelta, timezone
from pathlib import Path

from xeneon.contract import accept_sequence, validate_telemetry
from xeneon.poweroff import PoweroffServer, PoweroffState
from xeneon.session import Session
from xeneon.thresholds import next_level

ROOT = Path(__file__).resolve().parents[2]
CLIENT = ROOT / "client"


def sample(temp=64, sequence=1, status="ok", value=64, when=None):
    """Minimal host document. value is omitted from the reading when status is not ok."""
    when = when or datetime.now(timezone.utc)
    reading = {"status": status, "value": None if status != "ok" else value, "unit": "celsius", "binding": "configured", "verified": False}
    return {
        "schemaVersion": "1.0.0",
        "sampledOnce": False,
        "synthetic": False,
        "sequence": sequence,
        "sampledAtUtc": when.strftime("%Y-%m-%dT%H:%M:%S.%fZ"),
        "source": {"kind": "librehardwaremonitor", "synthetic": False},
        "poll": {"intervalSeconds": 1, "minimumIntervalSeconds": 0.5, "staleAfterSeconds": 5, "motherboardPolling": True, "acceptUniqueCandidates": False},
        "readings": {
            "cpu": {"temperature": reading, "utilization": {"total": {"status": "unmapped", "value": None, "unit": "percent", "binding": "none", "verified": False}, "logical": {"status": "unmapped", "items": []}}},
            "gpu": {"temperature": {"status": "missing", "value": None, "unit": "celsius", "binding": "none", "verified": False}, "load": {"status": "unmapped", "value": None, "unit": "percent", "binding": "none", "verified": False}, "vram": {"used": {"status": "unmapped", "value": None, "unit": "bytes", "binding": "none", "verified": False}, "total": {"status": "unmapped", "value": None, "unit": "bytes", "binding": "none", "verified": False}}},
            "memory": {"used": {"status": "unmapped", "value": None, "unit": "bytes", "binding": "none", "verified": False}, "total": {"status": "unmapped", "value": None, "unit": "bytes", "binding": "none", "verified": False}},
            "fans": {"cpu": {"status": "unmapped", "items": []}, "gpu": {"status": "unmapped", "items": []}, "case": {"status": "unmapped", "items": []}},
        },
    }


class ThresholdTests(unittest.TestCase):
    def test_initial_and_jumps(self):
        self.assertEqual(next_level(None, 64.9), "green")
        self.assertEqual(next_level(None, 65), "yellow")
        self.assertEqual(next_level(None, 75), "orange")
        self.assertEqual(next_level(None, 85), "red")
        self.assertEqual(next_level("green", 90), "red")
        self.assertEqual(next_level("red", 50), "green")

    def test_hysteresis_edges(self):
        self.assertEqual(next_level("yellow", 60), "green")
        self.assertEqual(next_level("yellow", 60.1), "yellow")
        self.assertEqual(next_level("orange", 70), "yellow")
        self.assertEqual(next_level("orange", 70.1), "orange")
        self.assertEqual(next_level("red", 80), "orange")
        self.assertEqual(next_level("red", 80.1), "red")
        self.assertEqual(next_level("green", 64), "green")


class ContractTests(unittest.TestCase):
    def test_missing_status_cannot_carry_zero(self):
        doc = sample(status="missing", value=0)
        doc["readings"]["cpu"]["temperature"]["value"] = 0
        doc["readings"]["cpu"]["temperature"]["status"] = "missing"
        self.assertTrue(validate_telemetry(doc))

    def test_ok_sample_is_accepted(self):
        self.assertEqual(validate_telemetry(sample()), [])

    def test_out_of_order_is_rejected_until_restart(self):
        older = datetime.now(timezone.utc)
        newer = older + timedelta(seconds=5)
        self.assertEqual(accept_sequence(5, newer, sample(sequence=3, when=older))[0], False)
        restarted = sample(sequence=0, when=newer + timedelta(seconds=5))
        self.assertEqual(accept_sequence(5, newer, restarted)[0], True)


class SessionTests(unittest.TestCase):
    def test_demo_is_labelled_and_live_failure_is_not_demo(self):
        demo = Session("demo")
        view = demo.view(datetime.now(timezone.utc), {})
        self.assertTrue(view["demo"])
        self.assertIn("DEMO DATA", view["banner"])
        live = Session("live")
        live.note_failure("unreachable", datetime.now(timezone.utc))
        failed = live.view(datetime.now(timezone.utc), {"status": "unavailable", "ssid": None, "bars": None, "detail": "none"})
        self.assertFalse(failed["demo"])
        self.assertIsNone(failed["cpu"]["temp"])
        self.assertNotIn("Night Drive", json.dumps(failed))

    def test_stale_hides_numbers(self):
        live = Session("live", stale_after=5)
        then = datetime.now(timezone.utc) - timedelta(seconds=30)
        reason = live.note_document(sample(when=then), datetime.now(timezone.utc))
        self.assertEqual(reason, "stale")
        self.assertIsNone(live.view(datetime.now(timezone.utc), {})["cpu"]["temp"])

    def test_pause_only_while_playing(self):
        demo = Session("demo")
        self.assertEqual(demo.demo_transport("play")[0], 409)
        self.assertEqual(demo.demo_transport("pause")[0], 200)
        self.assertFalse(demo.spotify["playing"])
        paused = demo.view(datetime.now(timezone.utc), {})
        self.assertFalse(paused["spotify"]["playing"])
        self.assertEqual(demo.demo_transport("pause")[0], 409)
        self.assertEqual(demo.demo_transport("play")[0], 200)
        self.assertEqual(demo.demo_transport("shell")[0], 403)


class PoweroffTests(unittest.TestCase):
    def test_auth_replay_and_only_poweroff(self):
        calls = []

        def helper():
            calls.append("sync-and-poweroff")

        state = PoweroffState(helper)
        server = PoweroffServer("127.0.0.1", 0, "shutdown-token", state, allow_lan=False)
        server.start()
        port = server.httpd.server_address[1]
        try:
            self.assertEqual(_post(port, None, {"action": "poweroff", "nonce": "n1", "issuedAtUtc": _now()}), 401)
            self.assertEqual(calls, [])
            self.assertEqual(_post(port, "shutdown-token", {"action": "reboot", "nonce": "n2", "issuedAtUtc": _now()}), 403)
            self.assertEqual(calls, [])
            self.assertEqual(_post(port, "shutdown-token", {"action": "poweroff", "nonce": "n3", "issuedAtUtc": _now()}), 202)
            self.assertEqual(_post(port, "shutdown-token", {"action": "poweroff", "nonce": "n3", "issuedAtUtc": _now()}), 409)
            deadline = time.time() + 2
            while time.time() < deadline and state.status()["state"] != "halt-issued":
                time.sleep(0.02)
            self.assertEqual(state.status()["state"], "halt-issued")
            self.assertEqual(calls, ["sync-and-poweroff"])
            self.assertEqual(_post(port, "shutdown-token", {"action": "poweroff", "nonce": "n4", "issuedAtUtc": _now()}), 202)
            time.sleep(0.05)
            self.assertEqual(calls, ["sync-and-poweroff"])
        finally:
            server.stop()


class ScriptTests(unittest.TestCase):
    def test_shell_scripts_parse_and_do_not_touch_boot_blindly(self):
        for name in ("install.sh", "uninstall.sh", "update.sh", "update-from-github.sh", "restart-bridge.sh", "kiosk.sh", "overlay-enable.sh", "overlay-disable.sh"):
            path = CLIENT / "scripts" / name
            subprocess.check_call(["bash", "-n", str(path)])
            text = path.read_text(encoding="utf-8")
            self.assertNotIn("iptables", text)
            self.assertNotIn("ufw disable", text)
        install = (CLIENT / "scripts" / "install.sh").read_text(encoding="utf-8")
        self.assertIn("not root", install)
        self.assertNotIn("/home/pi", install)
        self.assertIn("labwc", install)
        self.assertIn("sh \"$ROOT/scripts/restart-bridge.sh\"", install)
        restart = (CLIENT / "scripts" / "restart-bridge.sh").read_text(encoding="utf-8")
        self.assertNotIn("killall", restart)
        self.assertNotIn("pkill", restart)
        self.assertIn("xeneon", restart)
        overlay = (CLIENT / "scripts" / "overlay-enable.sh").read_text(encoding="utf-8")
        self.assertIn("--apply", overlay)
        completed = subprocess.run(["bash", str(CLIENT / "scripts" / "overlay-enable.sh"), "--check"], check=False, capture_output=True, text=True)
        self.assertNotEqual(completed.returncode, 0)
        self.assertFalse(Path("/boot/firmware/cmdline.txt").exists() and "overlayroot" in Path("/boot/firmware/cmdline.txt").read_text(encoding="utf-8"))

    def test_page_source_is_not_minified(self):
        script = (CLIENT / "xeneon" / "static" / "app.js").read_text(encoding="utf-8")
        self.assertGreater(script.count("\n"), 40)
        self.assertIn("function render", script)
        self.assertNotIn("test-token", script)
        self.assertIn("150ms", (CLIENT / "xeneon" / "static" / "app.css").read_text(encoding="utf-8"))

    def test_spotify_card_and_gear_menu_match_the_page(self):
        script = (CLIENT / "xeneon" / "static" / "app.js").read_text(encoding="utf-8")
        css = (CLIENT / "xeneon" / "static" / "app.css").read_text(encoding="utf-8")
        self.assertIn("object-fit: cover", css)
        self.assertIn("spot-row", css)
        self.assertIn("/icons/", script)
        self.assertIn("shuffle_on", script)
        self.assertIn("function closeGearMenu", script)
        self.assertNotIn("M14 4h6v6", script)
        icons = CLIENT / "xeneon" / "static" / "icons"
        for name in ("shuffle_off.png", "shuffle_on.png", "repeat_off.png", "repeat_on.png", "play.png", "pause.png", "previous.png", "next.png"):
            self.assertTrue((icons / name).is_file(), name)


def _now() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%fZ")


def _post(port: int, token: str | None, body: dict) -> int:
    data = json.dumps(body).encode("utf-8")
    request = urllib.request.Request("http://127.0.0.1:%d/v1/poweroff" % port, data=data, method="POST")
    request.add_header("Content-Type", "application/json")
    if token:
        request.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(request, timeout=2) as response:
            return response.status
    except urllib.error.HTTPError as exc:
        return exc.code


if __name__ == "__main__":
    unittest.main()
