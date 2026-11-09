"""Owns the dashboard browser process. The page cannot leave Chromium --kiosk.

The supervisor is client/scripts/kiosk.sh. It writes a pid file and a mode
file under the per-login runtime directory. Close dashboard sets mode
"paused" for this login only. The bridge, host token, and Spotify token
files are not touched. The next graphical login has a new runtime directory,
so boot autostart starts fullscreen again.

A process is the old launcher only when one of its arguments is a real
file path ending in scripts/kiosk.sh and it is not this update command.
Matching the letters kiosk.sh inside a shell command is not enough.
"""

from __future__ import annotations

import fcntl
import json
import os
import signal
import time
from pathlib import Path

MODES = {"fullscreen", "windowed", "paused"}
ACTIONS = {"fullscreen": "fullscreen", "windowed": "windowed", "close": "paused"}
VERBS = {"stop-old", "resume", "status", "supervise", "start", "handoff", "read-mode", "pause"}
DASHBOARD_PATH = "/"
TRANSITION_TIMEOUT_SECONDS = 8


def runtime_dir() -> Path:
    override = os.environ.get("XENEON_RUNTIME_DIR")
    if override:
        path = Path(override)
    else:
        xdg = os.environ.get("XDG_RUNTIME_DIR")
        if xdg:
            path = Path(xdg) / "xeneon"
        else:
            path = Path(f"/run/user/{os.getuid()}") / "xeneon"
    path.mkdir(parents=True, exist_ok=True)
    return path


def profile_dir() -> Path:
    override = os.environ.get("XENEON_BROWSER_PROFILE")
    if override:
        path = Path(override)
    else:
        base = os.environ.get("XDG_CONFIG_HOME")
        root = Path(base) if base else Path.home() / ".config"
        path = root / "xeneon" / "chromium-profile"
    path.mkdir(parents=True, exist_ok=True)
    return path


def browser_argv(browser: str, mode: str, port: int, profile: Path) -> list[str]:
    """Build the owned browser command. The URL is always the local dashboard.

    Chromium kiosk mode is a process flag. Leaving it means relaunching without
    --kiosk. Firefox is relaunched the same way with its own kiosk flag.
    """
    if mode not in {"fullscreen", "windowed"}:
        raise ValueError("mode")
    if browser.strip() == "":
        raise ValueError("browser")
    url = f"http://127.0.0.1:{port}/"
    name = Path(browser).name
    if "firefox" in name:
        if mode == "fullscreen":
            return [browser, "--kiosk", url, "-profile", str(profile)]
        return [browser, "--new-window", url, "-profile", str(profile)]
    argv = [
        browser,
        f"--user-data-dir={profile}",
        "--no-first-run",
        "--disable-infobars",
        f"--app={url}",
    ]
    if mode == "fullscreen":
        argv.append("--kiosk")
    else:
        argv.extend(["--new-window", "--window-size=1280,720"])
    return argv


def proc_args(pid: int, proc_root: Path | None = None) -> list[str] | None:
    root = proc_root or Path("/proc")
    path = root / str(pid) / "cmdline"
    try:
        raw = path.read_bytes()
    except OSError:
        return None
    if not raw:
        return None
    return [part.decode("utf-8", "replace") for part in raw.split(b"\0") if part]


def proc_ppid(pid: int, proc_root: Path | None = None) -> int | None:
    root = proc_root or Path("/proc")
    try:
        text = (root / str(pid) / "status").read_text(encoding="utf-8", errors="replace")
    except OSError:
        return None
    for line in text.splitlines():
        if line.startswith("PPid:"):
            parts = line.split()
            if len(parts) == 2 and parts[1].isdigit():
                return int(parts[1])
    return None


def ancestor_pids(pid: int, proc_root: Path | None = None) -> set[int]:
    found = {pid}
    current = pid
    for _ in range(32):
        parent = proc_ppid(current, proc_root)
        if parent is None or parent in found or parent <= 0:
            break
        found.add(parent)
        current = parent
    return found


def is_kiosk_script(argument: str) -> bool:
    """True only when the argument is an existing scripts/kiosk.sh file."""
    if not argument.endswith("/scripts/kiosk.sh"):
        return False
    try:
        return Path(argument).is_file()
    except OSError:
        return False


def is_old_launcher(args: list[str]) -> bool:
    """The pre-0.2.1 loop has the script path and no supervise/stop verb."""
    if any(part in VERBS for part in args):
        return False
    return any(is_kiosk_script(part) for part in args)


def is_owned_browser(args: list[str], profile: Path) -> bool:
    """True for the dedicated profile, not the user's everyday browser."""
    marker = f"--user-data-dir={profile}"
    local = any(
        part.startswith("--app=http://127.0.0.1:") or part.startswith("http://127.0.0.1:")
        for part in args
    )
    has_profile = marker in args or str(profile) in args
    return has_profile and local


def is_owned_main(args: list[str], profile: Path) -> bool:
    """The window process, not a renderer or GPU helper.

    Chromium's first process can exit after handing the same profile to an
    already-running window. That survivor is still an owned main process.
    A normal Chromium window does not use this profile and is not a match.
    """
    if any(part.startswith("--type=") for part in args):
        return False
    return is_owned_browser(args, profile)


def is_legacy_kiosk_child(args: list[str]) -> bool:
    """Browser started by the old loop: kiosk flag plus the local dashboard URL.

    Used only for a child of a verified kiosk.sh process. Firefox has no --app flag.
    A normal browser window is not a child of that script, so it is left alone.
    """
    joined = " ".join(args)
    local = "http://127.0.0.1:" in joined or "http://[::1]:" in joined
    return "--kiosk" in args and local


def is_supervisor_launcher(args: list[str]) -> bool:
    """The 0.2.1 supervisor: the real script path plus the supervise verb."""
    return "supervise" in args and any(is_kiosk_script(part) for part in args)


def is_bridge_process(args: list[str]) -> bool:
    """The dashboard bridge, not the kiosk helper and not another Python program."""
    for index, part in enumerate(args):
        if part == "-m" and index + 1 < len(args) and args[index + 1] == "xeneon":
            return True
        if part.endswith("/xeneon/__main__.py"):
            return True
    return False


def list_pids(proc_root: Path | None = None) -> list[int]:
    root = proc_root or Path("/proc")
    found = []
    try:
        entries = list(root.iterdir())
    except OSError:
        return found
    for entry in entries:
        if entry.name.isdigit():
            found.append(int(entry.name))
    return found


def old_launcher_pids(proc_root: Path | None = None, skip: set[int] | None = None) -> list[int]:
    skipped = skip or set()
    found = []
    for pid in list_pids(proc_root):
        if pid in skipped:
            continue
        args = proc_args(pid, proc_root)
        if args and is_old_launcher(args):
            found.append(pid)
    return found


def children_of(parent: int, proc_root: Path | None = None) -> list[int]:
    found = []
    for pid in list_pids(proc_root):
        if proc_ppid(pid, proc_root) == parent:
            found.append(pid)
    return found


def stop_old_launchers(proc_root: Path | None = None, skip: set[int] | None = None, killer=None) -> list[int]:
    """Stop verified old kiosk.sh loops and only their kiosk children.

    killer(pid) defaults to SIGTERM. Tests pass a recorder. The caller must
    put its own pid and ancestors in skip so an update script is not stopped
    for containing the script path in a different way.
    """
    skipped = set(skip or set())
    launchers = old_launcher_pids(proc_root, skipped)
    stopped: list[int] = []

    def default_kill(pid: int) -> None:
        os.kill(pid, signal.SIGTERM)

    kill = killer or default_kill
    for launcher in launchers:
        for child in children_of(launcher, proc_root):
            args = proc_args(child, proc_root) or []
            if is_legacy_kiosk_child(args) or is_owned_browser(args, profile_dir()):
                kill(child)
                stopped.append(child)
        kill(launcher)
        stopped.append(launcher)
    return stopped


def read_mode(directory: Path | None = None) -> str:
    path = (directory or runtime_dir()) / "mode"
    try:
        text = path.read_text(encoding="utf-8").strip()
    except OSError:
        return "fullscreen"
    return text if text in MODES else "fullscreen"


def read_applied_mode(directory: Path | None = None) -> str | None:
    path = (directory or runtime_dir()) / "applied-mode"
    try:
        text = path.read_text(encoding="utf-8").strip()
    except OSError:
        return None
    return text if text in MODES else None


def write_applied_mode(mode: str, directory: Path | None = None) -> None:
    if mode not in MODES:
        raise ValueError("mode")
    root = directory or runtime_dir()
    root.mkdir(parents=True, exist_ok=True)
    temporary = root / "applied-mode.tmp"
    temporary.write_text(mode + "\n", encoding="utf-8")
    temporary.replace(root / "applied-mode")


def read_revision(directory: Path | None = None) -> int:
    path = (directory or runtime_dir()) / "revision"
    try:
        text = path.read_text(encoding="utf-8").strip()
    except OSError:
        return 0
    return int(text) if text.isdigit() else 0


def write_revision(value: int, directory: Path | None = None) -> None:
    root = directory or runtime_dir()
    root.mkdir(parents=True, exist_ok=True)
    temporary = root / "revision.tmp"
    temporary.write_text(str(value) + "\n", encoding="utf-8")
    temporary.replace(root / "revision")


def write_mode(mode: str, directory: Path | None = None) -> None:
    if mode not in MODES:
        raise ValueError("mode")
    root = directory or runtime_dir()
    root.mkdir(parents=True, exist_ok=True)
    temporary = root / "mode.tmp"
    temporary.write_text(mode + "\n", encoding="utf-8")
    temporary.replace(root / "mode")


def supervisor_pid(directory: Path | None = None) -> int | None:
    path = (directory or runtime_dir()) / "supervisor.pid"
    try:
        text = path.read_text(encoding="utf-8").strip()
    except OSError:
        return None
    if not text.isdigit():
        return None
    return int(text)


def supervisor_is_ours(pid: int, proc_root: Path | None = None) -> bool:
    args = proc_args(pid, proc_root)
    if not args:
        return False
    return is_supervisor_launcher(args)


def owned_main_pids(profile: Path, proc_root: Path | None = None) -> list[int]:
    found = []
    for pid in list_pids(proc_root):
        args = proc_args(pid, proc_root) or []
        if is_owned_main(args, profile):
            found.append(pid)
    return sorted(found)


def retain_one_owned_main(profile: Path, proc_root: Path | None = None, killer=None) -> int | None:
    """Keep a single owned window. Extra mains are the launch loop, not helpers."""
    pids = owned_main_pids(profile, proc_root)
    if not pids:
        return None
    keep = pids[0]
    real = proc_root is None and killer is None
    kill = killer or (_signal_term if real else None)
    for pid in pids[1:]:
        if kill is not None:
            try:
                kill(pid)
            except OSError:
                pass
    return keep


def stop_owned_mains(profile: Path, proc_root: Path | None = None, killer=None) -> list[int]:
    """Stop every owned dashboard window. Other Chromium profiles are not signaled."""
    pids = owned_main_pids(profile, proc_root)
    if not pids:
        return []
    real = proc_root is None and killer is None
    kill = killer or (_signal_term if real else None)
    if kill is None:
        return pids
    for pid in pids:
        try:
            kill(pid)
        except OSError:
            pass
    if real:
        _wait_gone(pids, Path("/proc"))
    return pids


def _signal_term(pid: int) -> None:
    _signal(pid, signal.SIGTERM)


def _signal(pid: int, sig: int) -> None:
    os.kill(pid, sig)


def _owned_profile(profile: Path | None) -> Path:
    return profile if profile is not None else profile_dir()


def _stop_recorded_browser(directory: Path, proc_root: Path | None, kill, profile: Path) -> int | None:
    try:
        text = (directory / "browser.pid").read_text(encoding="utf-8").strip()
    except OSError:
        return None
    if not text.isdigit():
        return None
    pid = int(text)
    args = proc_args(pid, proc_root) or []
    if not is_owned_browser(args, profile):
        return None
    kill(pid)
    return pid


def handoff(
    proc_root: Path | None = None,
    skip: set[int] | None = None,
    killer=None,
    directory: Path | None = None,
    profile: Path | None = None,
) -> list[int]:
    """Stop old kiosk loops and an already-managed supervisor, then return.

    The caller starts one new supervisor after this returns. Ancestors of the
    caller stay in skip so the update script is not stopped for naming kiosk.sh.
    The bridge process is not a kiosk child and is not signaled here.
    On the real process table, this waits until those pids are gone so the new
    supervisor can take the lock. A test process table is not waited on.
    """
    skipped = set(skip or set())
    kill = killer or (lambda pid: _signal(pid, signal.SIGTERM))
    owned = _owned_profile(profile)
    runtime = directory or runtime_dir()
    stopped = stop_old_launchers(proc_root, skipped, kill)
    supervisors: list[int] = []
    for pid in list_pids(proc_root):
        if pid in skipped or pid in supervisors:
            continue
        args = proc_args(pid, proc_root)
        if args and is_supervisor_launcher(args):
            supervisors.append(pid)
    recorded = supervisor_pid(runtime)
    if recorded is not None and recorded not in skipped and recorded not in supervisors:
        if supervisor_is_ours(recorded, proc_root):
            supervisors.append(recorded)
    for launcher in supervisors:
        for child in children_of(launcher, proc_root):
            args = proc_args(child, proc_root) or []
            if is_legacy_kiosk_child(args) or is_owned_browser(args, owned):
                kill(child)
                stopped.append(child)
        recorded_browser = _stop_recorded_browser(runtime, proc_root, kill, owned)
        if recorded_browser is not None and recorded_browser not in stopped:
            stopped.append(recorded_browser)
        for main_pid in owned_main_pids(owned, proc_root):
            if main_pid in skipped or main_pid in stopped:
                continue
            kill(main_pid)
            stopped.append(main_pid)
        kill(launcher)
        stopped.append(launcher)
    if proc_root is None and killer is None and supervisors:
        _wait_gone(supervisors, Path("/proc"))
    return stopped


def _wait_gone(pids: list[int], proc_root: Path) -> None:
    import time

    deadline = time.time() + 2.0
    pending = list(pids)
    while pending and time.time() < deadline:
        pending = [pid for pid in pending if (proc_root / str(pid)).exists()]
        if pending:
            time.sleep(0.05)
    for pid in pending:
        try:
            _signal(pid, signal.SIGKILL)
        except OSError:
            pass


def read_transition(directory: Path | None = None) -> dict | None:
    path = (directory or runtime_dir()) / "transition.json"
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return None
    return data if isinstance(data, dict) else None


def write_transition(data: dict, directory: Path | None = None) -> None:
    root = directory or runtime_dir()
    root.mkdir(parents=True, exist_ok=True)
    temporary = root / "transition.json.tmp"
    temporary.write_text(json.dumps(data), encoding="utf-8")
    temporary.replace(root / "transition.json")


def reconcile_transition(directory: Path | None = None, now: float | None = None) -> dict | None:
    """Expire one unfinished change without starting another browser.

    The requested mode is put back to the mode the launcher last acknowledged.
    A later poll does not launch anything by itself.
    """
    root = directory or runtime_dir()
    moment = time.time() if now is None else now
    data = read_transition(root)
    if not data or data.get("status") != "pending":
        return data
    started = float(data.get("started") or 0)
    if moment - started < TRANSITION_TIMEOUT_SECONDS:
        return data
    applied = read_applied_mode(root)
    restore = applied if applied in {"fullscreen", "windowed"} else "fullscreen"
    write_mode(restore, root)
    failed = {
        "target": data.get("target"),
        "revision": data.get("revision") or read_revision(root),
        "started": started,
        "status": "failed",
        "detail": (
            "The window change did not finish. The dashboard was left in the last mode that actually started."
        ),
    }
    write_transition(failed, root)
    return failed


def acknowledge(mode: str, directory: Path | None = None) -> None:
    """Record that the owned launcher is really in this mode. Pending ends only here."""
    if mode not in MODES:
        raise ValueError("mode")
    root = directory or runtime_dir()
    write_applied_mode(mode, root)
    data = read_transition(root)
    if not data or data.get("status") != "pending":
        return
    if data.get("target") == mode:
        done = dict(data)
        done["status"] = "done"
        done["detail"] = ""
        write_transition(done, root)


def window_state(directory: Path | None = None, proc_root: Path | None = None, now: float | None = None) -> dict:
    """Label for the one window toggle. This is the launcher mode, not the DOM fullscreen API."""
    root = directory or runtime_dir()
    reconcile_transition(root, now)
    mode = read_mode(root)
    applied = read_applied_mode(root)
    revision = read_revision(root)
    data = read_transition(root)
    pid = supervisor_pid(root)
    managed = bool(pid is not None and supervisor_is_ours(pid, proc_root))
    pending = bool(data and data.get("status") == "pending")
    detail = ""
    if data and data.get("status") == "failed":
        detail = str(data.get("detail") or "")
    elif pending:
        detail = "Switching the owned window. The buttons stay off until that finishes."
    if not managed:
        return {
            "ok": True,
            "managed": False,
            "enabled": False,
            "closeEnabled": False,
            "pending": False,
            "signaled": False,
            "mode": mode,
            "appliedMode": applied,
            "revision": revision,
            "action": None,
            "label": "Fullscreen",
            "detail": (
                "This window was not opened by the Xeneon kiosk launcher. "
                "The fullscreen control and Close dashboard only change the browser that launcher started. "
                "The bridge keeps running."
            ),
        }
    if pending:
        label = "Switching…"
        action = None
        enabled = False
        close_enabled = False
    elif (applied or mode) == "fullscreen":
        label = "Exit fullscreen"
        action = "windowed"
        enabled = True
        close_enabled = True
    else:
        label = "Fullscreen"
        action = "fullscreen"
        enabled = True
        close_enabled = True
    return {
        "ok": True,
        "managed": True,
        "enabled": enabled,
        "closeEnabled": close_enabled,
        "pending": pending,
        "signaled": False,
        "mode": mode,
        "appliedMode": applied,
        "revision": revision,
        "action": action,
        "label": label,
        "detail": detail,
    }


def request_action(
    action: str,
    directory: Path | None = None,
    proc_root: Path | None = None,
    notifier=None,
    killer=None,
    now: float | None = None,
) -> dict:
    """Ask the owned supervisor to change window mode. No URL is accepted.

    One transition is signaled once. Further clicks, including from an older
    tab, are ignored until the launcher acknowledges that mode. A repeated
    Fullscreen request while already in kiosk used to kill the live window;
    Chromium then exited the new process after handing the profile to a
    surviving window, and the supervisor opened another window every couple
    of seconds.
    """
    del killer
    if action not in ACTIONS:
        return {
            "ok": False,
            "managed": False,
            "signaled": False,
            "unchanged": False,
            "enabled": False,
            "closeEnabled": False,
            "pending": False,
            "error": "action",
            "label": "Fullscreen",
            "action": None,
            "detail": "That window action is not available.",
        }
    root = directory or runtime_dir()
    root.mkdir(parents=True, exist_ok=True)
    moment = time.time() if now is None else now
    lock_path = root / "transition.lock"
    with lock_path.open("a+") as handle:
        fcntl.flock(handle.fileno(), fcntl.LOCK_EX)
        return _request_locked(action, root, proc_root, notifier, moment)


def _request_locked(action: str, root: Path, proc_root: Path | None, notifier, moment: float) -> dict:
    reconcile_transition(root, moment)
    state = window_state(root, proc_root, moment)
    if not state["managed"]:
        state["signaled"] = False
        state["unchanged"] = False
        return state
    if state["pending"]:
        state["signaled"] = False
        state["unchanged"] = True
        state["detail"] = "A window change is already in progress. The buttons stay off until it finishes."
        return state
    target = ACTIONS[action]
    applied = read_applied_mode(root)
    stable = applied if applied in MODES else read_mode(root)
    if target == stable and read_mode(root) == target:
        state = window_state(root, proc_root, moment)
        state["signaled"] = False
        state["unchanged"] = True
        state["detail"] = _unchanged_detail(target)
        return state
    previous = stable if stable in {"fullscreen", "windowed"} else "fullscreen"
    revision = read_revision(root) + 1
    write_mode(target, root)
    write_revision(revision, root)
    write_transition(
        {"target": target, "revision": revision, "started": moment, "status": "pending", "detail": ""},
        root,
    )
    pid = supervisor_pid(root)
    notify = notifier or (lambda target_pid: _signal(target_pid, signal.SIGUSR1))
    try:
        if pid is not None:
            notify(pid)
    except OSError:
        write_mode(previous, root)
        write_transition(
            {
                "target": target,
                "revision": revision,
                "started": moment,
                "status": "failed",
                "detail": "The kiosk launcher did not accept the request. The dashboard window may still be open.",
            },
            root,
        )
        state = window_state(root, proc_root, moment)
        state["signaled"] = False
        state["unchanged"] = False
        state["ok"] = False
        state["detail"] = "The kiosk launcher did not accept the request. The dashboard window may still be open."
        return state
    state = window_state(root, proc_root, moment)
    state["signaled"] = True
    state["unchanged"] = False
    state["revision"] = revision
    if action == "close":
        state["detail"] = (
            "Close was sent to the owned dashboard window. "
            "The buttons stay off until that window is actually closed."
        )
    elif action == "windowed":
        state["detail"] = "Switching the owned window out of kiosk. The buttons stay off until that finishes."
    else:
        state["detail"] = "Switching the owned window to fullscreen. The buttons stay off until that finishes."
    return state


def _unchanged_detail(target: str) -> str:
    if target == "fullscreen":
        return "Already fullscreen. The current window was left as it is."
    if target == "windowed":
        return "Already in a normal window. The current window was left as it is."
    return "Close was already requested. No further dashboard window was opened. The bridge keeps running."


def callback_page(success: bool) -> str:
    """Fixed local page. The address bar is replaced with / so the code is not kept."""
    if success:
        heading = "Spotify connected"
        text = "Spotify connected."
    else:
        heading = "Spotify did not connect"
        text = "Spotify did not connect. Return to the dashboard and use Connect Spotify again."
    return f"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>{heading}</title>
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta http-equiv="refresh" content="2;url=/">
<style>
body {{ margin: 0; min-height: 100vh; display: grid; place-items: center; background: #12141a; color: #e8eaef; font: 20px/1.4 system-ui, sans-serif; }}
main {{ width: min(36rem, calc(100% - 2rem)); }}
a {{ display: inline-block; margin-top: 1rem; padding: 0.8rem 1.1rem; background: #f4f6f8; color: #12141a; border-radius: 12px; text-decoration: none; font-weight: 650; }}
</style>
</head>
<body>
<main>
<h1>{heading}</h1>
<p>{text}</p>
<p><a href="{DASHBOARD_PATH}">Return to dashboard</a></p>
</main>
<script>
history.replaceState(null, "", "{DASHBOARD_PATH}");
setTimeout(function () {{ location.replace("{DASHBOARD_PATH}"); }}, 1200);
</script>
</body>
</html>
"""


def main(argv: list[str] | None = None) -> int:
    """Small command surface used by kiosk.sh. It does not take a URL."""
    import sys

    args = list(sys.argv[1:] if argv is None else argv)
    command = args[0] if args else ""
    if command == "browser-args":
        if len(args) != 4:
            print("usage: browser-args BROWSER MODE PORT", file=sys.stderr)
            return 2
        argv_out = browser_argv(args[1], args[2], int(args[3]), profile_dir())
        sys.stdout.buffer.write(b"\0".join(part.encode("utf-8") for part in argv_out))
        return 0
    if command in {"stop-old", "handoff"}:
        skip = ancestor_pids(os.getpid())
        stopped = handoff(skip=skip) if command == "handoff" else stop_old_launchers(skip=skip)
        print("stopped", " ".join(str(pid) for pid in stopped))
        return 0
    if command == "set-mode":
        if len(args) != 2 or args[1] not in MODES:
            return 2
        write_mode(args[1])
        return 0
    if command == "read-mode":
        print(read_mode())
        return 0
    if command == "read-applied":
        print(read_applied_mode() or "")
        return 0
    if command == "ack":
        if len(args) != 2 or args[1] not in MODES:
            return 2
        acknowledge(args[1])
        return 0
    if command == "main-pids":
        print(" ".join(str(pid) for pid in owned_main_pids(profile_dir())))
        return 0
    if command == "retain-one":
        kept = retain_one_owned_main(profile_dir())
        print("" if kept is None else kept)
        return 0
    if command == "stop-owned":
        stopped = stop_owned_mains(profile_dir())
        print("stopped", " ".join(str(pid) for pid in stopped))
        return 0
    if command == "reconcile":
        data = reconcile_transition()
        print("" if not data else data.get("status", ""))
        return 0
    if command == "exec-browser":
        if len(args) != 4:
            return 2
        argv_out = browser_argv(args[1], args[2], int(args[3]), profile_dir())
        os.execvp(argv_out[0], argv_out)
        return 1
    if command == "poke":
        pid = supervisor_pid()
        if pid and supervisor_is_ours(pid):
            os.kill(pid, signal.SIGUSR1)
            return 0
        return 1
    if command == "request":
        if len(args) != 2:
            return 2
        result = request_action(args[1])
        print(result.get("detail", ""))
        return 0 if result.get("signaled") or result.get("unchanged") else 1
    print("usage: stop-old | handoff | set-mode | read-mode | browser-args | request", file=sys.stderr)
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
