"""GPU temperature fallback and fan range rules."""

from __future__ import annotations

import unittest
from pathlib import Path
from tempfile import TemporaryDirectory

from xeneon.fans import apply_edit, catalog, default_pref, merge, observe
from xeneon.keyboard import toggle
from xeneon.session import _gpu_temperature
from xeneon.setupflow import SetupFlow


def _unmapped():
    return {"status": "unmapped", "value": None, "unit": "celsius", "binding": "none", "verified": False}


class GpuInventoryTests(unittest.TestCase):
    def test_one_gpu_core_fills_a_blank_role_and_two_do_not(self):
        doc = {
            "readings": {"gpu": {"temperature": _unmapped()}},
            "inventory": [{
                "sensorType": "Temperature",
                "name": "GPU Core",
                "hardwareType": "GpuNvidia",
                "status": "fresh",
                "normalizedValue": 63,
            }],
        }
        self.assertEqual(63.0, _gpu_temperature(doc))
        doc["inventory"].append(dict(doc["inventory"][0], normalizedValue=70))
        self.assertIsNone(_gpu_temperature(doc))
        doc["readings"]["gpu"]["temperature"] = {"status": "ok", "value": 55, "unit": "celsius", "binding": "semantic", "verified": False}
        self.assertEqual(55.0, _gpu_temperature(doc))
        doc["inventory"] = [{
            "sensorType": "Temperature",
            "name": "GPU Hot Spot",
            "hardwareType": "GpuNvidia",
            "status": "fresh",
            "normalizedValue": 80,
        }]
        doc["readings"]["gpu"]["temperature"] = _unmapped()
        self.assertIsNone(_gpu_temperature(doc))


class FanRuleTests(unittest.TestCase):
    def test_catalog_keeps_unknown_role_and_manual_range(self):
        sensors = catalog({"inventory": [
            {"id": "/lpc/fan/1", "sensorType": "Fan", "name": "Fan #2", "hardwareName": "Board", "hardwareType": "SuperIO", "status": "fresh", "normalizedValue": 800},
            {"id": "/cpu/temp", "sensorType": "Temperature", "name": "CPU", "status": "fresh", "normalizedValue": 40},
        ]})
        self.assertEqual(["/lpc/fan/1"], [item["id"] for item in sensors])
        pref = default_pref("/lpc/fan/1")
        observe(pref, 800)
        observe(pref, 1200)
        self.assertEqual((800, 1200, "observed"), (pref["minRpm"], pref["maxRpm"], pref["minMode"]))
        apply_edit(pref, {"op": "save", "minRpm": 400, "role": "case", "label": "Rear"})
        observe(pref, 500)
        self.assertEqual(400, pref["minRpm"])
        self.assertEqual("manual", pref["minMode"])
        self.assertEqual("case", pref["role"])
        apply_edit(pref, {"op": "save", "role": "header-3"})
        self.assertEqual("case", pref["role"])
        apply_edit(pref, {"op": "clear"})
        self.assertIsNone(pref["minRpm"])
        self.assertEqual(0, pref["samples"])
        rows = merge(sensors, {"/lpc/fan/1": pref})
        self.assertEqual("case", rows[0]["role"])
        self.assertEqual("Rear", rows[0]["label"])

    def test_theme_and_keyboard_do_not_touch_the_token(self):
        with TemporaryDirectory() as tmp:
            root = Path(tmp)
            token = root / "host.token"
            token.write_text("keep-me\n", encoding="utf-8")
            flow = SetupFlow(root / "client.json", token)
            status, payload = flow.handle("/api/setup/theme", {"theme": "day"}, None)
            self.assertEqual(200, status)
            self.assertEqual("day", payload["theme"])
            self.assertEqual("keep-me\n", token.read_text(encoding="utf-8"))
            again = SetupFlow(root / "client.json", token)
            self.assertEqual("day", again.theme)
            result = toggle("rm -rf /", root)
            self.assertFalse(result["ok"])
            self.assertIn("not available", result["detail"])


if __name__ == "__main__":
    unittest.main()
