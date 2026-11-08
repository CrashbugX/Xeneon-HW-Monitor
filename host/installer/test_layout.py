"""Installer source checks. They do not run the setup program and do not touch a service."""

from __future__ import annotations

import unittest
from pathlib import Path

NSI = Path(__file__).with_name("XeneonHost.nsi").read_text(encoding="utf-8")
ENABLE = Path(__file__).with_name("Enable-PiShutdown.ps1").read_text(encoding="utf-8")
FIREWALL = Path(__file__).with_name("FIREWALL.txt").read_text(encoding="utf-8")


class LayoutRules(unittest.TestCase):
    def test_product_identity_and_opt_in_sections(self):
        self.assertIn('!define PRODUCT_NAME "Xeneon HW Monitor Host"', NSI)
        self.assertIn('!define PRODUCT_VERSION "0.1.1"', NSI)
        self.assertIn("Section /o", NSI)
        self.assertIn("service.opted-in", NSI)
        service = NSI.split("Section /o", 1)[1].split("SectionEnd", 1)[0]
        self.assertNotIn("shutdown-config", service)
        self.assertNotIn("sc.exe start", NSI)
        self.assertIn("start= demand", NSI)

    def test_uninstall_keeps_programdata_and_uses_stop(self):
        uninstall = NSI.split('Section "un.Full"', 1)[1]
        self.assertIn("sc.exe stop XeneonTelemetryHost", uninstall)
        self.assertIn("sc.exe delete XeneonTelemetryHost", uninstall)
        self.assertNotIn("RMDir /r", uninstall.replace('RMDir /r "$INSTDIR"', ""))
        self.assertNotIn('RMDir /r "$DataDir"', NSI)

    def test_forbidden_side_effects_are_absent(self):
        commands = "\n".join(line for line in (NSI + "\n" + ENABLE).splitlines() if not line.strip().startswith(";") and not line.strip().startswith("#"))
        for banned in ("bcdedit", "Set-MpPreference", "netsh advfirewall", "DisableRealtimeMonitoring"):
            self.assertNotIn(banned, commands)
        self.assertIn("netsh advfirewall", FIREWALL)
        self.assertIn("192.168.1.40", ENABLE)
        self.assertIn("Poweroff was not armed", ENABLE)


if __name__ == "__main__":
    unittest.main()
