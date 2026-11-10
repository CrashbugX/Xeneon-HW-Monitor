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
from xeneon.fans import apply_edit, catalog, default_pref, merge, observe
from xeneon.spotify import PUBLIC_SPOTIFY_CLIENT_ID, redirect_uri, resolve_client_id
from xeneon.weather import clamp_brightness, clamp_opacity


class SetupFlow:
    def __init__(self, config_path: Path, token_path: Path) -> None:
        self.config_path = config_path
        self.token_path = token_path
        self.modules = {"cpu": True, "gpu": True, "load": True, "memory": True, "fans": True, "spotify": True}
        self.hosts: list[dict] = []
        self.client_id = PUBLIC_SPOTIFY_CLIENT_ID
        self.theme = "night"
        # auto follows sunrise/sunset. day and night are the gear override.
        self.theme_mode = "auto"
        self.right_module = "spotify"
        self.hour12 = True
        self.brightness = 100
        self.effects = True
        self.card_opacity = 78
        self.weather_place = ""
        self.weather_auto = True
        self.fan_prefs: dict = {}
        self.paired_name = ""
        self.saved_host = ""
        self.module_order = ["cpu", "gpu", "load", "memory", "fans", "spotify"]
        self.pi_shutdown = True
        self.control_port = 8766
        self._pairing_id = ""
        self._load()

    def appearance(self) -> dict:
        """Settings the page can show. No tokens."""
        return {
            "themeMode": self.theme_mode,
            "theme": self.theme,
            "rightModule": self.right_module,
            "hour12": self.hour12,
            "brightness": self.brightness,
            "effects": self.effects,
            "cardOpacity": self.card_opacity,
            "weatherPlace": self.weather_place,
            "weatherAuto": self.weather_auto,
        }

    def apply_appearance(self, body: dict) -> None:
        """Copy the fields the page sent. Unknown keys are ignored."""
        if "themeMode" in body:
            mode = str(body.get("themeMode") or "")
            if mode in {"auto", "day", "night"}:
                self.theme_mode = mode
                if mode in {"day", "night"}:
                    self.theme = mode
        if "rightModule" in body and body.get("rightModule") in {"spotify", "clock", "wifi"}:
            self.right_module = str(body.get("rightModule"))
        if "hour12" in body:
            self.hour12 = bool(body.get("hour12"))
        if "brightness" in body:
            self.brightness = clamp_brightness(body.get("brightness"), self.brightness)
        if "effects" in body:
            self.effects = bool(body.get("effects"))
        if "cardOpacity" in body:
            self.card_opacity = clamp_opacity(body.get("cardOpacity"), self.card_opacity)
        if "weatherAuto" in body:
            self.weather_auto = bool(body.get("weatherAuto"))
        if "weatherPlace" in body:
            self.weather_place = str(body.get("weatherPlace") or "").strip()[:80]

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
        if path == "/api/setup/pi-shutdown":
            self.pi_shutdown = bool(body.get("enabled", True))
            self._save()
            control = getattr(bridge, "control", None) if bridge is not None else None
            if control is not None:
                control.enabled = self.pi_shutdown
            return 200, {"ok": True, "piShutdown": self.pi_shutdown}
        if path == "/api/setup/pair":
            return self._pair(body, bridge)
        if path == "/api/setup/layout":
            modules = body.get("modules")
            if isinstance(modules, dict):
                for key in self.modules:
                    if key in modules:
                        self.modules[key] = bool(modules[key])
            order = body.get("order")
            if isinstance(order, list):
                self.module_order = [name for name in order if name in self.modules]
            self._save()
            if bridge is not None:
                bridge.session.modules = dict(self.modules)
            return 200, {"ok": True, "modules": self.modules, "moduleOrder": self.module_order}
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
        if path == "/api/setup/theme":
            theme = str(body.get("theme") or "")
            if theme not in {"auto", "day", "night"}:
                return 400, {"ok": False, "error": "theme"}
            self.theme_mode = theme
            if theme in {"day", "night"}:
                self.theme = theme
            self._save()
            return 200, {"ok": True, "theme": self.theme, "themeMode": self.theme_mode}
        if path == "/api/setup/appearance":
            self.apply_appearance(body)
            self._save()
            return 200, {"ok": True, **self.appearance()}
        if path == "/api/setup/fan":
            sensor_id = str(body.get("id") or "")
            if not sensor_id or len(sensor_id) > 180 or "/" not in sensor_id:
                return 400, {"ok": False, "error": "fan"}
            pref = dict(default_pref(sensor_id))
            stored = self.fan_prefs.get(sensor_id)
            if isinstance(stored, dict):
                pref.update(stored)
            self.fan_prefs[sensor_id] = apply_edit(pref, body)
            self._save()
            return 200, {"ok": True, "fan": self.fan_prefs[sensor_id]}
        return 404, {"ok": False, "error": "not_found"}

    def fan_rows(self, document: dict | None) -> list[dict]:
        return merge(catalog(document), self.fan_prefs)

    def observe_document(self, document: dict | None) -> None:
        changed = False
        for sensor in catalog(document):
            pref = dict(default_pref(sensor["id"]))
            stored = self.fan_prefs.get(sensor["id"])
            if isinstance(stored, dict):
                pref.update(stored)
            before = (pref.get("minRpm"), pref.get("maxRpm"), pref.get("samples"))
            observe(pref, sensor.get("rpm"))
            after = (pref.get("minRpm"), pref.get("maxRpm"), pref.get("samples"))
            if after != before:
                self.fan_prefs[sensor["id"]] = pref
                changed = True
        if changed:
            self._save()

    def _pair(self, body: dict, bridge) -> tuple[int, dict]:
        base = str(body.get("baseUrl") or "").rstrip("/")
        code = str(body.get("code") or "").strip()
        if not base.startswith("http://"):
            return 400, {
                "ok": False,
                "error": "address",
                "detail": "Choose the discovered PC first. Request code uses that selected host. The manual address is only for Find hosts.",
            }
        if not self._pairing_id:
            begin_url = base + "/v1/pair/begin"
            status, payload = _post(begin_url, {"displayName": socket.gethostname() or "xeneon-pi"})
            if status != 200:
                return status, _begin_failure(status, payload, begin_url)
            self._pairing_id = str(payload.get("pairingId") or "")
            return 200, {"ok": True, "step": "enter-code", "detail": "Approve the request on the Windows host, then enter the code shown there."}
        if not code:
            return 400, {"ok": False, "error": "code_required", "detail": "Enter the code shown on the Windows PC."}
        status, payload = _post(base + "/v1/pair/complete", {"pairingId": self._pairing_id, "code": code})
        if status != 200:
            error = payload.get("error", {})
            code_name = error.get("code") if isinstance(error, dict) else "pair_complete"
            message = error.get("message") if isinstance(error, dict) else ""
            if code_name != "awaiting_approval":
                self._pairing_id = ""
            detail = message or "The pairing code was not accepted."
            return status, {"ok": False, "error": code_name, "detail": detail}
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
        matched = ""
        for host in self.hosts:
            if str(host.get("baseUrl") or "").rstrip("/") == base:
                matched = str(host.get("name") or "")
        self._pairing_id = ""
        self.remember_host(base, matched)
        if self.pi_shutdown:
            from xeneon.shutdownlink import announce, ensure_token, host_ip
            shutdown = ensure_token(self.shutdown_path())
            announce(base, token, self.control_port, shutdown)
            if bridge is not None and getattr(bridge, "control", None) is not None:
                bridge.control.restrict_host = host_ip(base)
                bridge.shutdown_token = shutdown
        return 200, {"ok": True, "step": "paired"}

    def shutdown_path(self) -> Path:
        return self.config_path.parent / "shutdown.token"

    def remember_host(self, base: str, name: str = "") -> None:
        self.saved_host = base
        if name:
            self.paired_name = name
        self._save()

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
        theme = loaded.get("theme")
        self.theme = theme if theme in {"day", "night"} else "night"
        mode = loaded.get("themeMode")
        if mode in {"auto", "day", "night"}:
            self.theme_mode = mode
        elif theme in {"day", "night"}:
            # An older file picked a theme by hand. Keep that until they choose Auto.
            self.theme_mode = theme
        module = loaded.get("rightModule")
        if module in {"spotify", "clock", "wifi"}:
            self.right_module = module
        if "hour12" in loaded:
            self.hour12 = bool(loaded.get("hour12"))
        self.brightness = clamp_brightness(loaded.get("brightness"), 100)
        if "effects" in loaded:
            self.effects = bool(loaded.get("effects"))
        self.card_opacity = clamp_opacity(loaded.get("cardOpacity"), 78)
        self.weather_place = str(loaded.get("weatherPlace") or "")[:80]
        if "weatherAuto" in loaded:
            self.weather_auto = bool(loaded.get("weatherAuto"))
        prefs = loaded.get("fanPrefs")
        self.fan_prefs = prefs if isinstance(prefs, dict) else {}
        self.paired_name = str(loaded.get("pairedName") or "")
        self.saved_host = str(loaded.get("hostUrl") or "")
        if "piShutdown" in loaded:
            self.pi_shutdown = bool(loaded.get("piShutdown"))
        if loaded.get("controlPort"):
            self.control_port = int(loaded.get("controlPort") or 8766)
        order = loaded.get("moduleOrder")
        if isinstance(order, list):
            self.module_order = [name for name in order if name in self.modules]
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
        loaded["theme"] = self.theme
        loaded["themeMode"] = self.theme_mode
        loaded["rightModule"] = self.right_module
        loaded["hour12"] = self.hour12
        loaded["brightness"] = self.brightness
        loaded["effects"] = self.effects
        loaded["cardOpacity"] = self.card_opacity
        loaded["weatherPlace"] = self.weather_place
        loaded["weatherAuto"] = self.weather_auto
        loaded["fanPrefs"] = self.fan_prefs
        loaded["pairedName"] = self.paired_name
        loaded["moduleOrder"] = self.module_order
        loaded["piShutdown"] = self.pi_shutdown
        loaded["controlPort"] = self.control_port
        if self.saved_host:
            loaded["hostUrl"] = self.saved_host
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


def _begin_failure(status: int, payload: dict, url: str) -> dict:
    """Human text for a failed begin. The raw word pair_begin is not a status."""
    error = payload.get("error")
    if isinstance(error, dict):
        message = str(error.get("message") or "").strip()
        code = str(error.get("code") or "").strip() or "pair_begin"
        if message:
            return {"ok": False, "error": code, "detail": message}
    if status == 0:
        return {
            "ok": False,
            "error": "unreachable",
            "detail": (
                "The PC did not answer " + url + ". "
                "Finding it by discovery does not open this TCP port. "
                "The Windows window stays empty until this request arrives."
            ),
        }
    return {
        "ok": False,
        "error": "pair_begin",
        "detail": "The PC refused the pairing request. The Windows window stays empty until a request arrives.",
    }


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
