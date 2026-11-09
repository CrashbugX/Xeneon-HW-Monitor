"""Turns host documents into the view the page draws.

Live mode never copies demo numbers. A stale or rejected sample clears the
hero values. History only grows from fresh ok temperatures.
"""

from __future__ import annotations

from datetime import datetime, timedelta, timezone

from xeneon.contract import accept_sequence, parse_time, reading_value, validate_telemetry
from xeneon.thresholds import DEFAULT_FALL, DEFAULT_RISE, next_level

Link = str


class Session:
    """In-memory dashboard state. Safe to read from the HTTP thread after updates."""

    def __init__(self, mode: str, thresholds: dict | None = None, stale_after: float = 5.0) -> None:
        if mode not in ("live", "demo"):
            raise ValueError("mode must be live or demo")
        self.mode = mode
        self.rise = tuple((thresholds or {}).get("riseCelsius", DEFAULT_RISE))
        self.fall = tuple((thresholds or {}).get("fallCelsius", DEFAULT_FALL))
        self.stale_after = timedelta(seconds=stale_after)
        self.link: Link = "demo" if mode == "demo" else "connecting"
        self.cpu_level: str | None = None
        self.gpu_level: str | None = None
        self.cpu_history: list[float] = []
        self.gpu_history: list[float] = []
        self._sequence: int | None = None
        self._sampled_at: datetime | None = None
        self._document: dict | None = None
        self._ever = False
        self.spotify = _demo_spotify() if mode == "demo" else _disconnected_spotify()
        self.modules = {"cpu": True, "gpu": True, "load": True, "memory": True, "fans": True, "spotify": True}
        if mode == "demo":
            self._seed_demo()

    def document(self) -> dict | None:
        return self._document

    def note_failure(self, kind: str, now: datetime) -> None:
        """Record a failed poll. kind is connecting-class, not a reason to show demo data.

        Before the first good sample the link stays connecting, because the PC
        may still be booting. After a good sample, age past stale_after becomes
        disconnected. Telemetry loss does not power off the Pi.
        """
        if self.mode == "demo":
            return
        if kind == "unauthorized":
            self.link = "unauthorized"
            self._document = None
            return
        if not self._ever:
            self.link = "connecting"
            return
        if self._sampled_at is None or now - self._sampled_at > self.stale_after:
            self.link = "disconnected"
            self._document = None

    def note_document(self, doc: dict, now: datetime) -> str:
        """Try to accept one host document. Returns a short reason string."""
        if self.mode == "demo":
            return "demo"
        errors = validate_telemetry(doc)
        if errors:
            self.note_failure("invalid", now)
            return "invalid"
        accept, reason = accept_sequence(self._sequence, self._sampled_at, doc)
        if not accept:
            return reason
        sequence = doc.get("sequence")
        if isinstance(sequence, int):
            self._sequence = sequence
        self._sampled_at = parse_time(str(doc["sampledAtUtc"]))
        self._document = doc
        self._ever = True
        self._apply_levels(doc)
        self.link = "stale" if now - self._sampled_at > self.stale_after else "live"
        if self.link != "live":
            return "stale"
        return "live"

    def view(self, now: datetime, wifi: dict) -> dict:
        """Build the JSON /api/view document. Tokens are not copied into it."""
        if self.mode == "demo":
            return _demo_view(self, wifi)
        fresh = self.link == "live" and self._document is not None
        doc = self._document if fresh else None
        cpu_temp = _temp(doc, "cpu") if doc else None
        gpu_temp = _temp(doc, "gpu") if doc else None
        return {
            "mode": "live",
            "link": self.link,
            "demo": False,
            "banner": _banner(self.link, bool(doc and doc.get("synthetic"))),
            "thresholds": {"rise": list(self.rise), "fall": list(self.fall)},
            "wifi": wifi,
            "cpu": _cpu_block(doc, cpu_temp, self.cpu_level if fresh else None, self.cpu_history if fresh else []),
            "gpu": _gpu_block(doc, gpu_temp, self.gpu_level if fresh else None, self.gpu_history if fresh else []),
            "memory": _bytes_pair(doc, ("memory", "used"), ("memory", "total")) if doc else {"usedBytes": None, "totalBytes": None},
            "vram": _bytes_pair(doc, ("gpu", "vram", "used"), ("gpu", "vram", "total")) if doc else {"usedBytes": None, "totalBytes": None},
            "caseFans": _fans(doc, "case") if doc else [],
            "spotify": self.spotify,
            "modules": self.modules,
            "macrosEnabled": False,
            "hostSynthetic": bool(doc and doc.get("synthetic")),
        }

    def set_live_spotify(self, state: dict) -> None:
        """Replace the live Spotify card. Demo mode ignores this so a failure cannot become the example."""
        if self.mode == "demo":
            return
        self.spotify = state

    def demo_transport(self, action: str) -> tuple[int, str]:
        """Apply a local Spotify UI action. Refused unless mode is demo."""
        if self.mode != "demo":
            return 409, "demo_only"
        if action == "pause" and self.spotify["playing"]:
            self.spotify["playing"] = False
            return 200, "paused"
        if action == "play" and not self.spotify["playing"]:
            self.spotify["playing"] = True
            return 200, "playing"
        if action == "pause":
            return 409, "already_paused"
        if action == "play":
            return 409, "already_playing"
        if action == "shuffle":
            self.spotify["shuffle"] = "on" if self.spotify["shuffle"] == "off" else "off"
            return 200, self.spotify["shuffle"]
        if action == "repeat":
            order = ["off", "all", "one"]
            self.spotify["repeat"] = order[(order.index(self.spotify["repeat"]) + 1) % 3]
            return 200, self.spotify["repeat"]
        return 403, "action_not_allowed"

    def _apply_levels(self, doc: dict) -> None:
        cpu = _temp(doc, "cpu")
        gpu = _gpu_temperature(doc)
        if cpu is None:
            self.cpu_level = None
        else:
            self.cpu_level = next_level(self.cpu_level, cpu, self.rise, self.fall)
            self.cpu_history.append(cpu)
            self.cpu_history = self.cpu_history[-60:]
        if gpu is None:
            self.gpu_level = None
        else:
            self.gpu_level = next_level(self.gpu_level, gpu, self.rise, self.fall)
            self.gpu_history.append(gpu)
            self.gpu_history = self.gpu_history[-60:]

    def _seed_demo(self) -> None:
        self._document = {"inventory": [
            {"id": "/lpc/nct6798d/fan/0", "sensorType": "Fan", "name": "Fan #1", "hardwareName": "Nuvoton NCT6798D", "hardwareType": "SuperIO", "status": "fresh", "normalizedValue": 870},
            {"id": "/lpc/nct6798d/fan/1", "sensorType": "Fan", "name": "Fan #2", "hardwareName": "Nuvoton NCT6798D", "hardwareType": "SuperIO", "status": "fresh", "normalizedValue": 640},
            {"id": "/gpu-nvidia/0/fan/0", "sensorType": "Fan", "name": "GPU Fan", "hardwareName": "NVIDIA GeForce RTX 2080 SUPER", "hardwareType": "GpuNvidia", "status": "fresh", "normalizedValue": 1100},
        ]}
        self.cpu_history = [58, 60, 62, 64, 65, 68, 66, 64, 63, 64]
        self.gpu_history = [60, 61, 62, 63, 63, 64, 63, 63, 62, 63]
        self.cpu_level = next_level(None, self.cpu_history[-1], self.rise, self.fall)
        self.gpu_level = next_level(None, self.gpu_history[-1], self.rise, self.fall)


def _temp(doc: dict, side: str) -> float | None:
    return reading_value(_path(doc, "readings", side, "temperature"))


def _gpu_temperature(doc: dict) -> float | None:
    """Role value first. If that role is empty, one fresh inventory GPU Core.

    Hot Spot and Memory Junction are different sensors and are not used.
    More than one GPU Core match is left blank instead of picking one.
    """
    mapped = _temp(doc, "gpu")
    if mapped is not None:
        return mapped
    inventory = doc.get("inventory")
    if not isinstance(inventory, list):
        return None
    matches = []
    for sensor in inventory:
        if not isinstance(sensor, dict):
            continue
        if sensor.get("sensorType") != "Temperature" or sensor.get("name") != "GPU Core":
            continue
        if sensor.get("hardwareType") not in {"GpuNvidia", "GpuAmd", "GpuIntel"}:
            continue
        if sensor.get("status") != "fresh":
            continue
        value = sensor.get("normalizedValue")
        if isinstance(value, bool) or not isinstance(value, (int, float)):
            continue
        matches.append(float(value))
    if len(matches) == 1:
        return matches[0]
    return None


def _path(doc: dict, *keys: str) -> object:
    current: object = doc
    for key in keys:
        if not isinstance(current, dict):
            return None
        current = current.get(key)
    return current


def _cpu_block(doc: dict | None, temp: float | None, level: str | None, history: list[float]) -> dict:
    logical = [None] * 16
    total = None
    fans: list[dict] = []
    if doc is not None:
        total = reading_value(_path(doc, "readings", "cpu", "utilization", "total"))
        items = _path(doc, "readings", "cpu", "utilization", "logical", "items")
        if isinstance(items, list):
            for index, item in enumerate(items[:16]):
                logical[index] = reading_value(item)
        fans = _fans(doc, "cpu")
    return {"temp": temp, "level": level, "history": history, "loadTotal": total, "logical": logical, "fans": fans}


def _gpu_block(doc: dict | None, temp: float | None, level: str | None, history: list[float]) -> dict:
    load = reading_value(_path(doc, "readings", "gpu", "load")) if doc else None
    fans = _fans(doc, "gpu") if doc else []
    return {"temp": temp, "level": level, "history": history, "load": load, "fans": fans}


def _fans(doc: dict, group: str) -> list[dict]:
    block = _path(doc, "readings", "fans", group)
    items = block.get("items") if isinstance(block, dict) else None
    found = []
    if isinstance(items, list):
        for item in items:
            if not isinstance(item, dict):
                continue
            found.append({
                "name": item.get("sourceName") or group,
                "rpm": reading_value(item),
            })
    return found


def _bytes_pair(doc: dict, used_keys: tuple[str, ...], total_keys: tuple[str, ...]) -> dict:
    return {
        "usedBytes": reading_value(_path(doc, "readings", *used_keys)),
        "totalBytes": reading_value(_path(doc, "readings", *total_keys)),
    }


def _banner(link: str, host_synthetic: bool) -> str:
    if link == "connecting":
        return "CONNECTING · waiting for the Windows host"
    if link == "unauthorized":
        return "NOT AUTHORIZED · the host refused the token"
    if link == "stale":
        return "STALE · numbers hidden until a fresh sample"
    if link == "disconnected":
        return "DISCONNECTED · retrying"
    if host_synthetic:
        return "HOST FIXTURE · synthetic true · not this PC's sensors"
    return "LIVE · host sample"


def _disconnected_spotify() -> dict:
    return {
        "connected": False,
        "demo": False,
        "playing": False,
        "title": None,
        "artist": None,
        "album": None,
        "position": None,
        "duration": None,
        "shuffle": "off",
        "repeat": "off",
        "controlsEnabled": False,
    }


def _demo_spotify() -> dict:
    state = _disconnected_spotify()
    state.update({
        "demo": True,
        "playing": True,
        "title": "Night Drive",
        "artist": "Example Artist",
        "album": "Synthetic Album",
        "position": 98,
        "duration": 234,
        "controlsEnabled": True,
    })
    return state


def _demo_view(session: Session, wifi: dict) -> dict:
    logical = [18, 22, 31, 28, 40, 36, 33, 29, 41, 25, 19, 44, 38, 27, 21, 35]
    return {
        "mode": "demo",
        "link": "demo",
        "demo": True,
        "banner": "DEMO DATA · NOT LIVE · ALL VALUES ARE EXAMPLES",
        "thresholds": {"rise": list(session.rise), "fall": list(session.fall)},
        "wifi": {"status": "ok", "ssid": "Home Wi-Fi (example)", "bars": 3, "detail": "example"},
        "cpu": {
            "temp": session.cpu_history[-1],
            "level": session.cpu_level,
            "history": session.cpu_history,
            "loadTotal": 36,
            "logical": logical,
            "fans": [{"name": "CPU FAN", "rpm": 870}],
        },
        "gpu": {
            "temp": session.gpu_history[-1],
            "level": session.gpu_level,
            "history": session.gpu_history,
            "load": 42,
            "fans": [{"name": "GPU FAN 1", "rpm": 1140}, {"name": "FAN 2", "rpm": 1110}],
        },
        "memory": {"usedBytes": 18.4 * (1024 ** 3), "totalBytes": 32 * (1024 ** 3)},
        "vram": {"usedBytes": 4.5 * (1024 ** 3), "totalBytes": 8 * (1024 ** 3)},
        "caseFans": [
            {"name": "CASE 1", "rpm": 872},
            {"name": "CASE 2", "rpm": 906},
            {"name": "CASE 3", "rpm": None},
        ],
        "spotify": session.spotify,
        "modules": session.modules,
        "macrosEnabled": False,
        "hostSynthetic": False,
    }
