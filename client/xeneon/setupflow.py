"""Pi setup actions: discovery, pairing, layout, and the Spotify client id.

The host token is written to a mode-600 file and is not returned to the page.
"""

from __future__ import annotations

import json
import os
import socket
import urllib.request
from pathlib import Path

from xeneon.discover import discover
from xeneon.spotify import PUBLIC_SPOTIFY_CLIENT_ID, redirect_uri, resolve_client_id


class SetupFlow:
    def __init__(self, config_path: Path, token_path: Path) -> None:
        self.config_path = config_path
        self.token_path = token_path
        self.modules = {"cpu": True, "gpu": True, "load": True, "memory": True, "fans": True, "spotify": True}
        self.hosts: list[dict] = []
        self.client_id = PUBLIC_SPOTIFY_CLIENT_ID
        self._pairing_id = ""
        self._load()

    def redirect_uri(self, port: int) -> str:
        return redirect_uri(port)

    def handle(self, path: str, body: dict, bridge) -> tuple[int, dict]:
        if path == "/api/setup/discover":
            self.hosts = discover(timeout=0.4)
            manual = str(body.get("address") or "").strip()
            if manual:
                found = probe_manual(manual, int(body.get("port") or 8765))
                if found:
                    self.hosts.append(found)
            return 200, {"ok": True, "hosts": self.hosts}
        if path == "/api/setup/pair":
            return self._pair(body, bridge)
        if path == "/api/setup/layout":
            modules = body.get("modules")
            if isinstance(modules, dict):
                for key in self.modules:
                    if key in modules:
                        self.modules[key] = bool(modules[key])
                self._save()
                bridge.session.modules = dict(self.modules)
            return 200, {"ok": True, "modules": self.modules}
        if path == "/api/setup/spotify-id":
            raw = str(body.get("clientId") or "")
            if any(character.isspace() for character in raw) or len(raw) > 64:
                return 400, {"ok": False, "error": "client_id"}
            # Blank means the built-in public id. A nonempty value is kept as an override.
            self.client_id = resolve_client_id(raw)
            if bridge.spotify is not None:
                bridge.spotify.client_id = self.client_id
            self._save()
            return 200, {"ok": True, "spotifyClientId": self.client_id}
        if path == "/api/setup/spotify-begin":
            if bridge.spotify is None:
                return 409, {"ok": False, "error": "spotify_unavailable"}
            try:
                url = bridge.spotify.begin()
            except ValueError as exc:
                return 400, {"ok": False, "error": "needs_client_id", "detail": str(exc)}
            return 200, {"ok": True, "url": url}
        if path == "/api/setup/spotify-logout":
            if bridge.spotify is not None:
                bridge.spotify.logout()
            return 200, {"ok": True}
        return 404, {"ok": False, "error": "not_found"}

    def _pair(self, body: dict, bridge) -> tuple[int, dict]:
        base = str(body.get("baseUrl") or "").rstrip("/")
        code = str(body.get("code") or "").strip()
        if not base.startswith("http://"):
            return 400, {"ok": False, "error": "address"}
        if not self._pairing_id:
            status, payload = _post(base + "/v1/pair/begin", {"displayName": socket.gethostname() or "xeneon-pi"})
            if status != 200:
                return status, {"ok": False, "error": payload.get("error", {}).get("code", "pair_begin")}
            self._pairing_id = str(payload.get("pairingId") or "")
            return 200, {"ok": True, "step": "enter-code", "detail": "Approve the request on the Windows host, then enter the code shown there."}
        if not code:
            return 400, {"ok": False, "error": "code_required"}
        status, payload = _post(base + "/v1/pair/complete", {"pairingId": self._pairing_id, "code": code})
        if status != 200:
            error = payload.get("error", {})
            code_name = error.get("code") if isinstance(error, dict) else "pair_complete"
            if code_name != "awaiting_approval":
                self._pairing_id = ""
            return status, {"ok": False, "error": code_name}
        token = str(payload.get("deviceToken") or "")
        if not token:
            return 502, {"ok": False, "error": "missing_token"}
        self.token_path.parent.mkdir(parents=True, exist_ok=True)
        self.token_path.write_text(token + "\n", encoding="utf-8")
        try:
            self.token_path.chmod(0o600)
        except OSError:
            pass
        bridge.token = token
        bridge.host_url = base
        self._pairing_id = ""
        self._save()
        return 200, {"ok": True, "step": "paired"}

    def _load(self) -> None:
        if not self.config_path.exists():
            return
        try:
            loaded = json.loads(self.config_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            return
        if not isinstance(loaded, dict):
            return
        modules = loaded.get("modules")
        if isinstance(modules, dict):
            for key in self.modules:
                if key in modules:
                    self.modules[key] = bool(modules[key])
        stored = loaded.get("spotifyClientId")
        self.client_id = resolve_client_id(stored)
        if not (isinstance(stored, str) and stored.strip()):
            self._save()

    def _save(self) -> None:
        loaded: dict = {}
        if self.config_path.exists():
            try:
                current = json.loads(self.config_path.read_text(encoding="utf-8"))
                if isinstance(current, dict):
                    loaded = current
            except (OSError, json.JSONDecodeError):
                loaded = {}
        loaded["modules"] = self.modules
        loaded["spotifyClientId"] = self.client_id
        loaded["localPort"] = loaded.get("localPort") or 8734
        self.config_path.parent.mkdir(parents=True, exist_ok=True)
        temporary = self.config_path.with_suffix(".json.tmp")
        temporary.write_text(json.dumps(loaded, indent=2) + "\n", encoding="utf-8")
        os.replace(temporary, self.config_path)


def probe_manual(address: str, port: int) -> dict | None:
    if "://" in address or "/" in address or " " in address:
        return None
    url = f"http://{address}:{port}/v1/discover"
    try:
        with urllib.request.urlopen(url, timeout=1.5) as response:
            payload = json.loads(response.read().decode("utf-8"))
    except (OSError, json.JSONDecodeError, TimeoutError):
        return None
    if not isinstance(payload, dict):
        return None
    return {"name": payload.get("name") or address, "port": payload.get("port") or port, "baseUrl": f"http://{address}:{port}"}


def _post(url: str, payload: dict) -> tuple[int, dict]:
    data = json.dumps(payload).encode("utf-8")
    request = urllib.request.Request(url, data=data, headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(request, timeout=2) as response:
            body = json.loads(response.read().decode("utf-8") or "{}")
            return response.status, body if isinstance(body, dict) else {}
    except urllib.error.HTTPError as exc:
        raw = exc.read()
        try:
            body = json.loads(raw.decode("utf-8") or "{}")
        except json.JSONDecodeError:
            body = {}
        return exc.code, body if isinstance(body, dict) else {}
    except (OSError, json.JSONDecodeError, TimeoutError):
        return 0, {}
