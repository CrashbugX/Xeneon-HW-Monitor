"""Weather for the dashboard badge and the background.

Open-Meteo needs no API key. A failure leaves the last cache, or an empty
badge. Nothing here is required for temperatures from the Windows host.
"""

from __future__ import annotations

import json
import time
import urllib.parse
import urllib.request
from datetime import datetime, time as clock_time
from pathlib import Path

# Twelve minutes. The Pi should not ask the network on every dashboard frame.
REFRESH_SECONDS = 12 * 60
# Cards stay readable. 100 is solid. Below this the numbers wash out.
OPACITY_MIN = 68
OPACITY_MAX = 100
# Full brightness is 100. The floor keeps a dimmed panel from going black.
BRIGHTNESS_MIN = 20
BRIGHTNESS_MAX = 100

# WMO weather codes grouped into the five effects the page can draw.
_KINDS = {
    0: ("clear", "Clear"),
    1: ("clear", "Mainly clear"),
    2: ("cloudy", "Partly cloudy"),
    3: ("cloudy", "Overcast"),
    45: ("fog", "Fog"),
    48: ("fog", "Fog"),
    51: ("rain", "Drizzle"),
    53: ("rain", "Drizzle"),
    55: ("rain", "Drizzle"),
    61: ("rain", "Rain"),
    63: ("rain", "Rain"),
    65: ("rain", "Rain"),
    80: ("rain", "Showers"),
    81: ("rain", "Showers"),
    82: ("rain", "Showers"),
    95: ("storm", "Thunderstorm"),
    96: ("storm", "Thunderstorm"),
    99: ("storm", "Thunderstorm"),
}


def condition_from_code(code: int) -> tuple[str, str]:
    """Effect name and a short label. Unknown codes stay quiet and cloudy."""
    return _KINDS.get(int(code), ("cloudy", "Cloudy"))


def clamp_opacity(value: object, default: int = 78) -> int:
    """Percent. Solid is 100. The floor is OPACITY_MIN so type still reads."""
    try:
        number = int(value)  # type: ignore[arg-type]
    except (TypeError, ValueError):
        return default
    return max(OPACITY_MIN, min(OPACITY_MAX, number))


def clamp_brightness(value: object, default: int = 100) -> int:
    """Percent. 100 is no dim overlay."""
    try:
        number = int(value)  # type: ignore[arg-type]
    except (TypeError, ValueError):
        return default
    return max(BRIGHTNESS_MIN, min(BRIGHTNESS_MAX, number))


def resolve_theme(mode: str, moment: datetime, sunrise: clock_time | None, sunset: clock_time | None) -> str:
    """Day or night. Auto uses sunrise and sunset, or 07:00–19:00 when those are missing.

    An explicit day or night choice ignores the clock. That is the gear override.
    """
    if mode == "day":
        return "day"
    if mode == "night":
        return "night"
    current = moment.time()
    if sunrise is not None and sunset is not None and sunrise < sunset:
        return "day" if sunrise <= current < sunset else "night"
    return "day" if 7 <= moment.hour < 19 else "night"


def parse_clock(text: str) -> clock_time | None:
    """Open-Meteo sends '2026-11-09T07:12'. Only the clock part is used."""
    if not text or "T" not in text:
        return None
    clock = text.split("T", 1)[1][:5]
    try:
        hour, minute = clock.split(":")
        return clock_time(int(hour), int(minute))
    except (ValueError, IndexError):
        return None


def parse_forecast(payload: dict, place: str) -> dict | None:
    """Pull the current reading and today's sunrise and sunset. None if the shape is wrong."""
    current = payload.get("current") if isinstance(payload, dict) else None
    daily = payload.get("daily") if isinstance(payload, dict) else None
    if not isinstance(current, dict) or not isinstance(daily, dict):
        return None
    try:
        code = int(current.get("weather_code"))
        temp = float(current.get("temperature_2m"))
        humidity = int(current.get("relative_humidity_2m"))
    except (TypeError, ValueError):
        return None
    sun = daily.get("sunrise") or [None]
    moon = daily.get("sunset") or [None]
    kind, label = condition_from_code(code)
    return {
        "ok": True,
        "tempC": round(temp),
        "humidity": humidity,
        "code": code,
        "kind": kind,
        "label": label,
        "place": place or "Nearby",
        "sunrise": sun[0] if sun else None,
        "sunset": moon[0] if moon else None,
    }


def parse_place(payload: dict) -> tuple[float, float, str] | None:
    """First geocoding hit: latitude, longitude, and the place name Open-Meteo returned."""
    results = payload.get("results") if isinstance(payload, dict) else None
    if not isinstance(results, list) or not results:
        return None
    hit = results[0]
    if not isinstance(hit, dict):
        return None
    try:
        lat = float(hit["latitude"])
        lon = float(hit["longitude"])
    except (KeyError, TypeError, ValueError):
        return None
    name = str(hit.get("name") or "Nearby")
    return lat, lon, name


def parse_ip_location(payload: dict) -> tuple[float, float, str] | None:
    """ipwho.is style body. A failed lookup is None, not an exception."""
    if not isinstance(payload, dict) or payload.get("success") is False:
        return None
    try:
        lat = float(payload["latitude"])
        lon = float(payload["longitude"])
    except (KeyError, TypeError, ValueError):
        return None
    name = str(payload.get("city") or "Nearby")
    return lat, lon, name


def demo_weather() -> dict:
    """Labelled sample. No network, and no real place name."""
    return {
        "ok": True,
        "demo": True,
        "tempC": 24,
        "humidity": 40,
        "code": 0,
        "kind": "clear",
        "label": "Clear",
        "place": "Demo",
        "sunrise": None,
        "sunset": None,
        "stale": False,
    }


class WeatherService:
    """Cache one forecast. The page reads snapshot(); refresh() talks to the network."""

    def __init__(self, cache_path: Path, place: str = "", auto: bool = True) -> None:
        self.cache_path = cache_path
        self.place = place.strip()
        self.auto = auto
        self._cache: dict | None = None
        self._fetched = 0.0
        self._load()

    def configure(self, place: str, auto: bool) -> None:
        self.place = place.strip()
        self.auto = auto

    def snapshot(self, now: float | None = None) -> dict:
        moment = time.time() if now is None else now
        if self._cache is None:
            return {"ok": False, "stale": True, "detail": "Weather has not loaded yet."}
        body = dict(self._cache)
        body["stale"] = (moment - self._fetched) > REFRESH_SECONDS
        body["demo"] = False
        return body

    def due(self, now: float | None = None) -> bool:
        moment = time.time() if now is None else now
        return self._cache is None or (moment - self._fetched) >= REFRESH_SECONDS

    def refresh(self, transport, now: float | None = None) -> dict:
        """transport(url) returns a parsed JSON object or raises. Tests pass a fake."""
        moment = time.time() if now is None else now
        located = self._locate(transport)
        if located is None:
            return self.snapshot(moment)
        lat, lon, name = located
        query = urllib.parse.urlencode({
            "latitude": lat,
            "longitude": lon,
            "current": "temperature_2m,relative_humidity_2m,weather_code",
            "daily": "sunrise,sunset",
            "timezone": "auto",
            "forecast_days": 1,
        })
        payload = transport("https://api.open-meteo.com/v1/forecast?" + query)
        parsed = parse_forecast(payload, name)
        if parsed is None:
            return self.snapshot(moment)
        parsed["ok"] = True
        self._cache = parsed
        self._fetched = moment
        self._store()
        return self.snapshot(moment)

    def _locate(self, transport):
        if self.place and not self.auto:
            found = parse_place(transport(
                "https://geocoding-api.open-meteo.com/v1/search?count=1&name=" + urllib.parse.quote(self.place)
            ))
            return found
        try:
            return parse_ip_location(transport("https://ipwho.is/"))
        except (OSError, ValueError, TypeError, KeyError):
            return None

    def _load(self) -> None:
        try:
            raw = json.loads(self.cache_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            return
        if isinstance(raw, dict) and isinstance(raw.get("body"), dict):
            self._cache = raw["body"]
            self._fetched = float(raw.get("fetched") or 0)

    def _store(self) -> None:
        self.cache_path.parent.mkdir(parents=True, exist_ok=True)
        blob = {"fetched": self._fetched, "body": self._cache}
        temporary = self.cache_path.with_suffix(".json.tmp")
        temporary.write_text(json.dumps(blob), encoding="utf-8")
        temporary.replace(self.cache_path)
