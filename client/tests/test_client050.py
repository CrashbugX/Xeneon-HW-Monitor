"""Client 0.5.0: weather, theme clock, Wi-Fi actions, and the opacity floor."""

import unittest
from datetime import datetime

from xeneon.weather import (
    clamp_brightness,
    clamp_opacity,
    condition_from_code,
    parse_forecast,
    parse_ip_location,
    resolve_theme,
)
from xeneon.wifi import parse_saved_connections, parse_wifi_table, plan_wifi_action, split_nmcli


class WeatherRuleTests(unittest.TestCase):
    def test_codes_and_opacity_floor(self):
        self.assertEqual("clear", condition_from_code(0)[0])
        self.assertEqual("storm", condition_from_code(95)[0])
        self.assertEqual("cloudy", condition_from_code(999)[0])
        self.assertEqual(68, clamp_opacity(10))
        self.assertEqual(100, clamp_opacity(140))
        self.assertEqual(20, clamp_brightness(0))
        self.assertEqual(100, clamp_brightness(100))

    def test_auto_theme_uses_sunrise_and_a_fixed_fallback(self):
        from datetime import time
        morning = datetime(2026, 11, 10, 8, 0)
        late = datetime(2026, 11, 10, 21, 0)
        self.assertEqual("day", resolve_theme("auto", morning, time(7, 0), time(18, 0)))
        self.assertEqual("night", resolve_theme("auto", late, time(7, 0), time(18, 0)))
        self.assertEqual("day", resolve_theme("day", late, time(7, 0), time(18, 0)))
        self.assertEqual("night", resolve_theme("auto", datetime(2026, 11, 10, 6, 0), None, None))
        self.assertEqual("day", resolve_theme("auto", datetime(2026, 11, 10, 12, 0), None, None))

    def test_forecast_parse_does_not_invent_a_place(self):
        parsed = parse_forecast({
            "current": {"temperature_2m": 21.2, "relative_humidity_2m": 55, "weather_code": 61},
            "daily": {"sunrise": ["2026-11-10T07:05"], "sunset": ["2026-11-10T17:40"]},
        }, "Nearby")
        self.assertEqual(21, parsed["tempC"])
        self.assertEqual("rain", parsed["kind"])
        self.assertEqual("Nearby", parsed["place"])
        self.assertIsNone(parse_forecast({"current": {}}, "Nearby"))
        self.assertIsNone(parse_ip_location({"success": False}))


class WifiActionTests(unittest.TestCase):
    def test_nmcli_split_and_forget_needs_confirmation(self):
        self.assertEqual(["yes", "A:B", "80"], split_nmcli(r"yes:A\:B:80"))
        rows = parse_wifi_table("yes:Lab:80:WPA2\nno:Guest:40:\n")
        self.assertEqual("Lab", rows[0]["ssid"])
        self.assertTrue(rows[1]["open"])
        saved = parse_saved_connections("Lab:802-11-wireless:wlan0\nWired:802-3-ethernet:eth0\n")
        self.assertEqual(["Lab"], saved)
        blocked = plan_wifi_action("forget", "Lab", False, "", ["Lab"], ["Lab"])
        self.assertEqual("confirm_required", blocked["error"])
        allowed = plan_wifi_action("forget", "Lab", True, "", ["Lab"], ["Lab"])
        self.assertEqual(["nmcli", "connection", "delete", "Lab"], allowed["argv"])
        secret = plan_wifi_action("connect", "Guest", False, "secret-pass", [], ["Guest"])
        self.assertIn("secret-pass", secret["argv"])
        self.assertNotIn("secret-pass", secret.get("error", ""))
        unknown = plan_wifi_action("connect", "Other", False, "", [], ["Guest"])
        self.assertEqual("unknown_network", unknown["error"])


if __name__ == "__main__":
    unittest.main()
