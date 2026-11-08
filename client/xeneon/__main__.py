"""Start the localhost dashboard bridge.

Usage:
  python3 -m xeneon --demo --local-port 8734
  python3 -m xeneon --config /home/pi/.config/xeneon/client.json

Demo mode never contacts a host and never starts the poweroff listener.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

from xeneon.bridge import Bridge
from xeneon.poweroff import PoweroffServer, PoweroffState
from xeneon.session import Session
from xeneon.setupflow import SetupFlow
from xeneon.spotify import SpotifySession


def main(argv: list[str] | None = None) -> int:
    """Parse arguments and block until Ctrl+C. Returns a process exit code."""
    parser = argparse.ArgumentParser(description="Xeneon Pi dashboard bridge")
    parser.add_argument("--config", default="")
    parser.add_argument("--demo", action="store_true")
    parser.add_argument("--local-port", type=int, default=0)
    args = parser.parse_args(argv)
    config = _load(args.config) if args.config else {}
    mode = "demo" if args.demo or config.get("mode") == "demo" else "live"
    port = args.local_port or int(config.get("localPort") or 8734)
    session = Session(mode, config.get("displayThresholds"), float(config.get("staleAfterSeconds") or 5))
    config_path = Path(args.config) if args.config else _default_config()
    token_path = Path(config.get("tokenFile") or (config_path.parent / "host.token"))
    token = ""
    host_url = config.get("hostUrl")
    if mode == "live":
        token = _read_token(str(token_path))
    setup = SetupFlow(config_path, token_path)
    session.modules = dict(setup.modules)
    spotify = SpotifySession(config_path.parent / "spotify.json", setup.client_id, port)
    bridge = Bridge(session, host_url, token, port, setup, spotify)
    control = None
    if mode == "live" and config.get("controlBind"):
        shutdown_token = _read_token(config.get("shutdownTokenFile") or "")
        if shutdown_token:
            state = PoweroffState(_real_helper)
            try:
                control = PoweroffServer(
                    config["controlBind"],
                    int(config.get("controlPort") or 8766),
                    shutdown_token,
                    state,
                    bool(config.get("controlAllowLan")),
                )
            except ValueError as exc:
                print(str(exc), file=sys.stderr)
                return 2
            except OSError as exc:
                print(f"control listener failed: {exc}", file=sys.stderr)
                return 1
    bridge.start()
    if control is not None:
        control.start()
    print(f"dashboard http://127.0.0.1:{port}/ mode={mode}", file=sys.stderr)
    try:
        bridge.page_thread.join()
    except KeyboardInterrupt:
        pass
    finally:
        bridge.stop()
        if control is not None:
            control.stop()
    return 0


def _default_config() -> Path:
    root = Path(os.environ.get("XDG_CONFIG_HOME") or (Path.home() / ".config"))
    return root / "xeneon" / "client.json"


def _load(path: str) -> dict:
    return json.loads(Path(path).read_text(encoding="utf-8"))


def _read_token(path: str) -> str:
    if not path:
        return ""
    file = Path(path)
    if not file.is_file():
        print(f"token file missing: {path}", file=sys.stderr)
        return ""
    text = file.read_text(encoding="utf-8").strip()
    return text.splitlines()[0].strip() if text else ""


def _real_helper() -> None:
    """Run the fixed poweroff helper. No shell and no extra arguments."""
    import subprocess

    subprocess.run(["sudo", "-n", "/usr/local/sbin/xeneon-poweroff"], check=False)


if __name__ == "__main__":
    raise SystemExit(main())
