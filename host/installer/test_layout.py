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
        self.assertIn('!define PRODUCT_VERSION "0.2.0"', NSI)
        self.assertIn("XeneonHwMonitor.exe", NSI)
        self.assertIn("Repair-PrivateFirewall.ps1", NSI)
        self.assertNotIn("localport=8765", NSI)
        self.assertIn("/DELETEDATA", NSI)
        self.assertIn("taskkill /IM XeneonHwMonitor.exe", NSI)
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
        self.assertIn('${If} $DeleteData == "1"', uninstall)
        self.assertIn('RMDir /r "$DataDir"', uninstall)
        self.assertIn('StrCpy $DeleteData "0"', NSI)
        self.assertNotIn('RMDir /r "$PROFILE"', NSI)

    def test_forbidden_side_effects_are_absent(self):
        commands = "\n".join(line for line in (NSI + "\n" + ENABLE).splitlines() if not line.strip().startswith(";") and not line.strip().startswith("#"))
        for banned in ("bcdedit", "Set-MpPreference", "DisableRealtimeMonitoring"):
            self.assertNotIn(banned, commands)
        for line in commands.splitlines():
            if "netsh advfirewall" in line and "delete rule" not in line:
                self.fail(line)
        repair = Path(__file__).with_name("Repair-PrivateFirewall.ps1").read_text(encoding="utf-8")
        self.assertIn("profile=private", repair)
        self.assertIn("remoteip=localsubnet", repair)
        self.assertIn("protocol=TCP", repair)
        self.assertIn("protocol=UDP", repair)
        self.assertIn("publishPort", repair)
        self.assertIn("discoveryPort", repair)
        self.assertNotIn("profile=public", repair)
        self.assertNotIn("opmode disable", repair.lower())
        self.assertNotIn("Start-Process", repair)
        service = NSI.split("Section /o", 1)[1].split("SectionEnd", 1)[0]
        self.assertNotIn("netsh", service)
        self.assertIn("netsh advfirewall", FIREWALL)
        self.assertIn("192.168.1.40", ENABLE)
        self.assertIn("Poweroff was not armed", ENABLE)


if __name__ == "__main__":
    unittest.main()
