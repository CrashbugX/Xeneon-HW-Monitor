"""Kiosk ownership, window mode, and the Spotify return page."""

from __future__ import annotations

import json
import os
import stat
import subprocess
import tempfile
import threading
import time
import unittest
import urllib.error
import urllib.request
from io import BytesIO
from pathlib import Path

from xeneon.bridge import Bridge
from xeneon.kioskctl import (
    acknowledge,
    apply_launched_mode,
    browser_argv,
    callback_page,
    handoff,
    is_bridge_process,
    is_old_launcher,
    old_launcher_pids,
    owned_main_pids,
    request_action,
    stop_old_launchers,
    window_state,
    write_applied_mode,
    write_mode,
)
from xeneon.session import Session


class _Capture:
    def __init__(self) -> None:
        self.path = "/"
        self.headers = {}
        self.wfile = BytesIO()
        self.status = 0

    def send_response(self, status: int) -> None:
        self.status = status

    def send_header(self, key: str, value: str) -> None:
        return

    def end_headers(self) -> None:
        return


def _proc(root: Path, pid: int, args: list[str], ppid: int = 1) -> None:
    process = root / str(pid)
    process.parent.mkdir(parents=True, exist_ok=True)
    process.mkdir()
    (process / "cmdline").write_bytes(b"\0".join(part.encode() for part in args) + b"\0")
    (process / "status").write_text(f"Name: test\nPPid:\t{ppid}\n", encoding="utf-8")


class KioskIdentityTests(unittest.TestCase):
    def test_old_loop_matches_a_real_script_path_only(self):
        with tempfile.TemporaryDirectory() as tmp:
            script = Path(tmp) / "client" / "scripts" / "kiosk.sh"
            script.parent.mkdir(parents=True)
            script.write_text("#!/bin/sh\n", encoding="utf-8")
            self.assertTrue(is_old_launcher(["sh", str(script)]))
            self.assertFalse(is_old_launcher(["sh", str(script), "supervise"]))
            self.assertFalse(is_old_launcher(["sh", str(script), "stop-old"]))
            self.assertFalse(is_old_launcher(["bash", "-c", "echo scripts/kiosk.sh"]))
            self.assertFalse(is_old_launcher(["sh", str(tmp) + "/missing/scripts/kiosk.sh"]))

    def test_two_old_launchers_and_their_kiosk_children_are_stopped(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            script = root / "client" / "scripts" / "kiosk.sh"
            script.parent.mkdir(parents=True)
            script.write_text("#!/bin/sh\n", encoding="utf-8")
            proc = root / "proc"
            _proc(proc, 10, ["sh", str(script)], 1)
            _proc(proc, 11, ["chromium", "--kiosk", "--app=http://127.0.0.1:8734/"], 10)
            _proc(proc, 20, ["sh", str(script)], 1)
            _proc(proc, 21, ["chromium", "--kiosk", "--app=http://127.0.0.1:8734/"], 20)
            _proc(proc, 22, ["firefox", "--kiosk", "http://127.0.0.1:8734/"], 20)
            _proc(proc, 23, ["python3", "-m", "xeneon", "--config", "client.json"], 20)
            _proc(proc, 30, ["chromium", "https://example.com"], 1)
            killed = []
            stopped = stop_old_launchers(proc, skip={999}, killer=killed.append)
            self.assertEqual(old_launcher_pids(proc), [10, 20])
            self.assertCountEqual(stopped, [11, 10, 22, 21, 20])
            self.assertNotIn(30, stopped)
            self.assertNotIn(23, stopped)

    def test_handoff_stops_the_managed_supervisor_and_keeps_other_browsers(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            script = root / "client" / "scripts" / "kiosk.sh"
            script.parent.mkdir(parents=True)
            script.write_text("#!/bin/sh\n", encoding="utf-8")
            profile = root / "profile"
            proc = root / "proc"
            runtime = root / "runtime"
            runtime.mkdir()
            _proc(proc, 10, ["sh", str(script)], 1)
            _proc(proc, 11, ["firefox", "--kiosk", "http://127.0.0.1:8734/"], 10)
            _proc(proc, 40, ["sh", str(script), "supervise"], 1)
            _proc(proc, 41, ["chromium", f"--user-data-dir={profile}", "--app=http://127.0.0.1:8734/"], 40)
            _proc(proc, 42, ["python3", "-m", "xeneon"], 40)
            _proc(proc, 50, ["sh", str(script), "handoff"], 7)
            _proc(proc, 30, ["chromium", "--kiosk", "https://example.com"], 1)
            (runtime / "supervisor.pid").write_text("40\n", encoding="utf-8")
            (runtime / "browser.pid").write_text("41\n", encoding="utf-8")
            killed = []
            stopped = handoff(proc, skip={50, 7}, killer=killed.append, directory=runtime, profile=profile)
            self.assertIn(10, stopped)
            self.assertIn(11, stopped)
            self.assertIn(40, stopped)
            self.assertIn(41, stopped)
            self.assertNotIn(42, stopped)
            self.assertNotIn(50, stopped)
            self.assertNotIn(30, stopped)
            self.assertTrue(is_bridge_process(["python3", "-m", "xeneon", "--config", "x"]))
            self.assertFalse(is_bridge_process(["python3", "-m", "xeneon.kioskctl", "handoff"]))
            self.assertFalse(is_bridge_process(["python3", "-c", "print('xeneon')"]))

    def test_tokens_survive_a_window_request(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            runtime = root / "runtime"
            runtime.mkdir()
            config = root / "config"
            config.mkdir()
            token = config / "host.token"
            spotify = config / "spotify.json"
            token.write_text("host-token\n", encoding="utf-8")
            spotify.write_text('{"refresh_token":"keep"}\n', encoding="utf-8")
            script = Path(__file__).resolve().parents[1] / "scripts" / "kiosk.sh"
            proc = root / "proc"
            _proc(proc, 40, ["sh", str(script), "supervise"], 1)
            (runtime / "supervisor.pid").write_text("40\n", encoding="utf-8")
            result = request_action("close", runtime, proc, notifier=self._refuse, now=1000)
            self.assertTrue(result["managed"])
            self.assertFalse(result["signaled"])
            self.assertFalse(result["pending"])
            self.assertEqual(token.read_text(encoding="utf-8"), "host-token\n")
            self.assertIn("keep", spotify.read_text(encoding="utf-8"))
            applied = request_action("close", runtime, proc, notifier=lambda pid: None, now=1001)
            self.assertTrue(applied["signaled"])
            self.assertTrue(applied["pending"])
            self.assertFalse(applied["enabled"])
            self.assertFalse(applied["closeEnabled"])
            self.assertEqual(applied["mode"], "paused")
            self.assertEqual((runtime / "mode").read_text(encoding="utf-8").strip(), "paused")
            self.assertEqual(token.read_text(encoding="utf-8"), "host-token\n")

    @staticmethod
    def _refuse(pid: int) -> None:
        raise OSError(pid)

    def test_label_follows_acked_mode_and_clicks_do_not_stack(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            runtime = root / "runtime"
            runtime.mkdir()
            script = Path(__file__).resolve().parents[1] / "scripts" / "kiosk.sh"
            proc = root / "proc"
            _proc(proc, 40, ["sh", str(script), "supervise"], 1)
            _proc(proc, 41, ["chromium", "--type=renderer", "--user-data-dir=" + str(root / "profile"), "--app=http://127.0.0.1:8734/"], 40)
            _proc(proc, 42, ["chromium", "--user-data-dir=" + str(root / "profile"), "--app=http://127.0.0.1:8734/"], 40)
            self.assertEqual(owned_main_pids(root / "profile", proc), [42])
            (runtime / "supervisor.pid").write_text("40\n", encoding="utf-8")
            write_mode("fullscreen", runtime)
            write_applied_mode("fullscreen", runtime)
            signals = []

            def notify(pid):
                signals.append(pid)

            shown = window_state(runtime, proc, now=500)
            self.assertEqual("Exit fullscreen", shown["label"])
            self.assertEqual("windowed", shown["action"])
            self.assertTrue(shown["enabled"])
            first = request_action("windowed", runtime, proc, notifier=notify, now=1000)
            self.assertTrue(first["signaled"])
            self.assertTrue(first["pending"])
            self.assertEqual("Switching…", first["label"])
            self.assertFalse(first["enabled"])
            self.assertFalse(first["closeEnabled"])
            reloaded = window_state(runtime, proc, now=1001)
            self.assertTrue(reloaded["pending"])
            self.assertEqual("Switching…", reloaded["label"])
            for name in ("fullscreen", "windowed", "close"):
                extra = request_action(name, runtime, proc, notifier=notify, now=1002)
                self.assertFalse(extra["signaled"])
                self.assertTrue(extra["pending"])
            self.assertEqual([40], signals)
            acknowledge("windowed", runtime)
            done = window_state(runtime, proc, now=1003)
            self.assertFalse(done["pending"])
            self.assertTrue(done["enabled"])
            self.assertEqual("Fullscreen", done["label"])
            self.assertEqual("fullscreen", done["action"])
            idle = request_action("windowed", runtime, proc, notifier=notify, now=1004)
            self.assertTrue(idle["unchanged"])
            self.assertFalse(idle["signaled"])
            self.assertEqual([40], signals)
            expired = window_state(runtime, proc, now=1004)
            self.assertFalse(expired["pending"])
            request_action("fullscreen", runtime, proc, notifier=notify, now=2000)
            self.assertEqual([40, 40], signals)
            late = window_state(runtime, proc, now=2008)
            self.assertFalse(late["pending"])
            self.assertTrue(late["enabled"])
            self.assertIn("did not finish", late["detail"])
            self.assertEqual("windowed", late["appliedMode"])
            self.assertEqual("Fullscreen", late["label"])
            self.assertEqual([40, 40], signals)
            stayed = request_action("windowed", runtime, proc, notifier=notify, now=3000)
            self.assertFalse(stayed["signaled"])
            self.assertEqual([40, 40], signals)

    def test_chromium_kiosk_and_window_commands_are_different(self):
        profile = Path("/tmp/xeneon-owned-profile")
        full = browser_argv("chromium", "fullscreen", 8734, profile)
        window = browser_argv("chromium", "windowed", 8734, profile)
        self.assertNotIn("--kiosk", full)
        self.assertIn("--start-fullscreen", full)
        self.assertNotIn("--kiosk", window)
        self.assertTrue(any(part.startswith("--app=http://127.0.0.1:8734/") for part in window))
        self.assertTrue(any(part.startswith("--user-data-dir=") for part in window))
        self.assertNotIn("https://", " ".join(window))
        firefox = browser_argv("firefox", "windowed", 8734, profile)
        self.assertNotIn("--kiosk", firefox)
        self.assertIn("http://127.0.0.1:8734/", firefox)

    def test_callback_returns_only_to_the_dashboard_root(self):
        page = callback_page(True)
        self.assertIn('href="/"', page)
        self.assertIn('location.replace("/")', page)
        self.assertNotIn("code=", page)
        self.assertNotIn("state=", page)
        self.assertNotIn("access_token", page)
        error = callback_page(False)
        self.assertIn("Return to dashboard", error)
        self.assertNotIn("http://192.168.", error)


class WindowApiTests(unittest.TestCase):
    def test_window_action_rejects_a_foreign_origin_and_unknown_action(self):
        bridge = Bridge(Session("demo"), None, "", 0)
        thread = threading.Thread(target=bridge.httpd.serve_forever, daemon=True)
        thread.start()
        port = bridge.httpd.server_address[1]
        try:
            setup = json.loads(urllib.request.urlopen(f"http://127.0.0.1:{port}/api/setup", timeout=2).read())
            foreign = urllib.request.Request(
                f"http://127.0.0.1:{port}/api/window",
                data=b'{"action":"close"}',
                headers={
                    "Content-Type": "application/json",
                    "Origin": "http://192.168.1.50",
                    "X-Xeneon-Setup": setup["csrf"],
                },
            )
            with self.assertRaises(urllib.error.HTTPError) as caught:
                urllib.request.urlopen(foreign, timeout=2)
            self.assertEqual(403, caught.exception.code)
            missing = urllib.request.Request(
                f"http://127.0.0.1:{port}/api/window",
                data=b'{"action":"close"}',
                headers={"Content-Type": "application/json", "X-Xeneon-Setup": setup["csrf"]},
            )
            with self.assertRaises(urllib.error.HTTPError) as bare:
                urllib.request.urlopen(missing, timeout=2)
            self.assertEqual(403, bare.exception.code)
            lan_host = urllib.request.Request(
                f"http://127.0.0.1:{port}/api/window",
                data=b'{"action":"close"}',
                headers={
                    "Content-Type": "application/json",
                    "Origin": f"http://127.0.0.1:{port}",
                    "Host": f"192.168.1.20:{port}",
                    "X-Xeneon-Setup": setup["csrf"],
                },
            )
            with self.assertRaises(urllib.error.HTTPError) as hosted:
                urllib.request.urlopen(lan_host, timeout=2)
            self.assertEqual(403, hosted.exception.code)
            unknown = urllib.request.Request(
                f"http://127.0.0.1:{port}/api/window",
                data=b'{"action":"open-url","url":"http://evil.example"}',
                headers={
                    "Content-Type": "application/json",
                    "Origin": f"http://127.0.0.1:{port}",
                    "X-Xeneon-Setup": setup["csrf"],
                },
            )
            with self.assertRaises(urllib.error.HTTPError) as bad:
                urllib.request.urlopen(unknown, timeout=2)
            self.assertEqual(400, bad.exception.code)
            body = json.loads(bad.exception.read().decode())
            self.assertEqual("action", body["error"])
            self.assertNotIn("evil.example", json.dumps(body))
            class _Spotify:
                def finish(self, code, state):
                    raise ValueError(code)

            bridge.spotify = _Spotify()
            handler = _Capture()
            handler.path = "/spotify/callback?code=secret-code&state=secret-state"
            bridge.finish_spotify(handler)
            page = handler.wfile.getvalue().decode()
            self.assertNotIn("secret-code", page)
            self.assertNotIn("secret-state", page)
            self.assertIn('location.replace("/")', page)
        finally:
            bridge.stop()


class HandoffTests(unittest.TestCase):
    def test_a_starter_that_exits_is_not_launched_again(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            runtime = root / "runtime"
            runtime.mkdir()
            log = root / "browser.log"
            browser = root / "fake-browser"
            browser.write_text(
                "#!/bin/sh\n"
                "printf '%s\\n' \"$*\" >> \"$XENEON_FAKE_LOG\"\n"
                "case \" $* \" in\n"
                "  *' --hold '*) sleep 30 ;;\n"
                "  *) \"$0\" --hold \"$@\" >/dev/null 2>&1 & ;;\n"
                "esac\n",
                encoding="utf-8",
            )
            browser.chmod(browser.stat().st_mode | stat.S_IEXEC)
            profile = root / "profile"
            previous = {key: os.environ.get(key) for key in ("XENEON_RUNTIME_DIR", "XENEON_BROWSER_PROFILE", "XENEON_FAKE_LOG")}
            os.environ["XENEON_RUNTIME_DIR"] = str(runtime)
            os.environ["XENEON_BROWSER_PROFILE"] = str(profile)
            os.environ["XENEON_FAKE_LOG"] = str(log)
            try:
                first = apply_launched_mode(str(browser), 8734, profile)
                self.assertTrue(first["stable"])
                parents = [line for line in log.read_text(encoding="utf-8").splitlines() if "--hold" not in line]
                self.assertEqual(1, len(parents))
                second = apply_launched_mode(str(browser), 8734, profile)
                self.assertTrue(second["stable"])
                parents = [line for line in log.read_text(encoding="utf-8").splitlines() if "--hold" not in line]
                self.assertEqual(1, len(parents))
                write_mode("windowed", runtime)
                third = apply_launched_mode(str(browser), 8734, profile)
                self.assertTrue(third["stable"])
                parents = [line for line in log.read_text(encoding="utf-8").splitlines() if "--hold" not in line]
                self.assertEqual(1, len(parents))
                self.assertFalse(any("--new-window" in line for line in parents))
            finally:
                from xeneon.kioskctl import stop_owned_mains
                stop_owned_mains(profile)
                for key, value in previous.items():
                    if value is None:
                        os.environ.pop(key, None)
                    else:
                        os.environ[key] = value


class FakeBrowserSmokeTests(unittest.TestCase):
    def test_supervisor_relaunches_without_kiosk_then_stays_paused(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            runtime = root / "runtime"
            log = root / "browser.log"
            browser = root / "fake-browser"
            browser.write_text(
                "#!/bin/sh\nprintf '%s\\n' \"$*\" >> \"$XENEON_FAKE_LOG\"\n"
                "if echo \" $* \" | grep -q ' --kiosk '; then sleep 30; else sleep 30; fi\n",
                encoding="utf-8",
            )
            browser.chmod(browser.stat().st_mode | stat.S_IEXEC)
            env = os.environ.copy()
            env.update({
                "XENEON_RUNTIME_DIR": str(runtime),
                "XENEON_BROWSER_PROFILE": str(root / "profile"),
                "XENEON_BROWSER": str(browser),
                "XENEON_FAKE_LOG": str(log),
                "PYTHONPATH": str(Path(__file__).resolve().parents[1]),
                "DISPLAY": "",
                "WAYLAND_DISPLAY": "",
            })
            script = Path(__file__).resolve().parents[1] / "scripts" / "kiosk.sh"
            proc = subprocess.Popen(["sh", str(script), "supervise"], env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            try:
                for _ in range(50):
                    if log.exists() and "--start-fullscreen" in log.read_text(encoding="utf-8"):
                        break
                    import time
                    time.sleep(0.05)
                self.assertIn("--start-fullscreen", log.read_text(encoding="utf-8"))
                before = log.read_text(encoding="utf-8").count("\n")
                subprocess.check_call(["python3", "-m", "xeneon.kioskctl", "request", "fullscreen"], env=env)
                time.sleep(5)
                self.assertEqual(before, log.read_text(encoding="utf-8").count("\n"))
                subprocess.check_call(["python3", "-m", "xeneon.kioskctl", "set-mode", "windowed"], env=env)
                subprocess.check_call(["python3", "-m", "xeneon.kioskctl", "poke"], env=env)
                import time
                time.sleep(1.2)
                text = log.read_text(encoding="utf-8")
                self.assertNotIn("--new-window", text)
                self.assertEqual(before, text.count("\n"))
                subprocess.check_call(["python3", "-m", "xeneon.kioskctl", "set-mode", "paused"], env=env)
                subprocess.check_call(["python3", "-m", "xeneon.kioskctl", "poke"], env=env)
                time.sleep(0.6)
                after = log.read_text(encoding="utf-8").count("\n")
                time.sleep(0.8)
                self.assertEqual(after, log.read_text(encoding="utf-8").count("\n"))
                second = subprocess.run(
                    ["sh", str(script), "supervise"],
                    env=env,
                    capture_output=True,
                    text=True,
                    timeout=5,
                    check=False,
                )
                self.assertEqual(0, second.returncode)
                self.assertIn("already running", second.stdout + second.stderr)
                subprocess.check_call(["sh", str(script), "resume"], env=env)
                time.sleep(2)
                resumed = log.read_text(encoding="utf-8")
                self.assertGreater(resumed.count("--start-fullscreen"), 0)
                self.assertGreater(resumed.count("\n"), after)
            finally:
                proc.send_signal(subprocess.signal.SIGTERM)
                proc.wait(timeout=3)
                subprocess.run(["python3", "-m", "xeneon.kioskctl", "stop-owned"], env=env, check=False)

    def test_exiting_starter_does_not_open_another_window(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            runtime = root / "runtime"
            log = root / "browser.log"
            browser = root / "fake-browser"
            browser.write_text(
                "#!/bin/sh\n"
                "printf '%s\\n' \"$*\" >> \"$XENEON_FAKE_LOG\"\n"
                "if printf '%s' \" $* \" | grep -q ' --hold '; then sleep 60; exit 0; fi\n"
                "\"$0\" --hold \"$@\" >/dev/null 2>&1 &\n"
                "exit 0\n",
                encoding="utf-8",
            )
            browser.chmod(browser.stat().st_mode | stat.S_IEXEC)
            env = os.environ.copy()
            env.update({
                "XENEON_RUNTIME_DIR": str(runtime),
                "XENEON_BROWSER_PROFILE": str(root / "profile"),
                "XENEON_BROWSER": str(browser),
                "XENEON_FAKE_LOG": str(log),
                "PYTHONPATH": str(Path(__file__).resolve().parents[1]),
                "DISPLAY": "",
                "WAYLAND_DISPLAY": "",
            })
            script = Path(__file__).resolve().parents[1] / "scripts" / "kiosk.sh"
            proc = subprocess.Popen(["sh", str(script), "supervise"], env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            try:
                import time
                for _ in range(50):
                    if log.exists() and "--start-fullscreen" in log.read_text(encoding="utf-8"):
                        break
                    time.sleep(0.05)
                def parents():
                    return [line for line in log.read_text(encoding="utf-8").splitlines() if "--hold" not in line]
                for _ in range(20):
                    if len(parents()) >= 1:
                        break
                    time.sleep(0.05)
                time.sleep(5)
                self.assertEqual(1, len(parents()))
                for _ in range(5):
                    subprocess.check_call(["python3", "-m", "xeneon.kioskctl", "request", "fullscreen"], env=env)
                time.sleep(3)
                self.assertEqual(1, len(parents()))
                for _ in range(5):
                    subprocess.check_call(["python3", "-m", "xeneon.kioskctl", "request", "windowed"], env=env)
                time.sleep(2)
                self.assertEqual(0, len([line for line in parents() if "--new-window" in line]))
                time.sleep(3)
                self.assertEqual(1, len(parents()))
            finally:
                proc.send_signal(subprocess.signal.SIGTERM)
                proc.wait(timeout=3)
                subprocess.run(["python3", "-m", "xeneon.kioskctl", "stop-owned"], env=env, check=False)


if __name__ == "__main__":
    unittest.main()
