"""Spotify Web API remote control. Authorization code with PKCE.

The client secret is never read or stored. Playback uses the official player
endpoints. This module does not stream audio and does not collect a password.
"""

from __future__ import annotations

import base64
import hashlib
import json
import secrets
import string
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

# Public identifier for the app registered at developer.spotify.com.
# It is not a client secret. A saved nonempty id replaces it.
PUBLIC_SPOTIFY_CLIENT_ID = "c15bb4b8de984a84a831995690451695"

AUTHORIZE_URL = "https://accounts.spotify.com/authorize"
TOKEN_URL = "https://accounts.spotify.com/api/token"
PLAYER_URL = "https://api.spotify.com/v1/me/player"
SCOPES = "user-read-playback-state user-read-currently-playing user-modify-playback-state"
ART_PREFIXES = ("https://i.scdn.co/", "https://image-cdn-ak.spotifycdn.com/", "https://mosaic.scdn.co/")


def resolve_client_id(value: object) -> str:
    """Use a saved id when it has characters. Missing or blank keeps the built-in public id."""
    if isinstance(value, str) and value.strip():
        return value.strip()
    return PUBLIC_SPOTIFY_CLIENT_ID


def redirect_uri(port: int) -> str:
    """Loopback callback. Spotify allows HTTP only for 127.0.0.1 or ::1, not localhost or a LAN address."""
    return f"http://127.0.0.1:{port}/spotify/callback"


def make_verifier() -> str:
    alphabet = string.ascii_letters + string.digits + "-._~"
    return "".join(secrets.choice(alphabet) for _ in range(64))


def challenge_s256(verifier: str) -> str:
    digest = hashlib.sha256(verifier.encode("ascii")).digest()
    return base64.urlsafe_b64encode(digest).decode("ascii").rstrip("=")


class SpotifySession:
    """Holds the PKCE handshake and the token file. HTTP is injectable for tests."""

    def __init__(self, token_path: Path, client_id: str, port: int, transport=None) -> None:
        self.token_path = token_path
        self.client_id = client_id.strip()
        self.port = port
        self.transport = transport or UrllibTransport()
        self._pending: dict | None = None
        self._retry_after = 0.0
        self.state = _load(token_path)

    def needs_client_id(self) -> bool:
        return self.client_id == ""

    def logged_in(self) -> bool:
        return bool(self.state.get("refresh_token") or self.state.get("access_token"))

    def account_name(self) -> str:
        """Spotify display name when the token can read it. Empty if the profile call is refused."""
        cached = self.state.get("display_name")
        if isinstance(cached, str) and cached:
            return cached
        token = self.state.get("access_token")
        if not token:
            return ""
        status, body, _headers = self.transport.request(
            "GET",
            "https://api.spotify.com/v1/me",
            None,
            {"Authorization": "Bearer " + token},
        )
        if status != 200 or not isinstance(body, dict):
            return ""
        name = str(body.get("display_name") or body.get("id") or "")
        if name:
            self.state["display_name"] = name
            self.token_path.parent.mkdir(parents=True, exist_ok=True)
            self.token_path.write_text(json.dumps(self.state), encoding="utf-8")
        return name

    def begin(self, now: float | None = None) -> str:
        """Return the Spotify consent URL. Raises ValueError when the client id is missing."""
        if self.needs_client_id():
            raise ValueError("A Spotify Client ID is required. The client secret is not used.")
        now = time.time() if now is None else now
        verifier = make_verifier()
        state = secrets.token_urlsafe(24)
        self._pending = {"verifier": verifier, "state": state, "expires": now + 600, "used": False}
        query = urllib.parse.urlencode({
            "client_id": self.client_id,
            "response_type": "code",
            "redirect_uri": redirect_uri(self.port),
            "state": state,
            "scope": SCOPES,
            "code_challenge_method": "S256",
            "code_challenge": challenge_s256(verifier),
        })
        return AUTHORIZE_URL + "?" + query

    def finish(self, code: str, state: str, now: float | None = None) -> None:
        """Exchange a callback code once. Rejects a mismatched, expired, or replayed state."""
        now = time.time() if now is None else now
        pending = self._pending
        if pending is None or pending.get("used") or pending.get("state") != state or now > pending["expires"]:
            raise ValueError("Spotify returned a state that does not match this login attempt.")
        if not code:
            raise ValueError("Spotify did not return an authorization code.")
        pending["used"] = True
        self._pending = None
        payload = {
            "grant_type": "authorization_code",
            "code": code,
            "redirect_uri": redirect_uri(self.port),
            "client_id": self.client_id,
            "code_verifier": pending["verifier"],
        }
        status, body, _headers = self.transport.request("POST", TOKEN_URL, form(payload), None)
        if status != 200:
            raise ValueError(_error_message(status, body) or "Spotify token exchange failed.")
        self._store_token(body, now)

    def logout(self) -> None:
        """Forget the local tokens. Spotify has no PKCE revoke call in the public Web API."""
        self.state = {}
        self._pending = None
        if self.token_path.exists():
            self.token_path.unlink()

    def poll(self, now: float | None = None) -> dict:
        """Return a dashboard spotify object. A paused track keeps its cover. Idle has none."""
        now = time.time() if now is None else now
        if self.needs_client_id():
            return _view(False, "Add the Spotify Client ID in Setup. Do not paste a client secret.", None)
        if not self.logged_in():
            return _view(False, "Not connected. Connect Spotify opens the official consent page in this browser.", None)
        if now < self._retry_after:
            return _view(True, "Spotify asked us to wait before the next request.", None, rate_limited=True)
        self._refresh_if_needed(now)
        if not self.state.get("access_token"):
            return _view(False, "Login expired. Connect Spotify again.", None)
        status, body, headers = self.transport.request(
            "GET",
            PLAYER_URL,
            None,
            {"Authorization": "Bearer " + self.state["access_token"]},
        )
        if status == 401:
            self._refresh(now, force=True)
            status, body, headers = self.transport.request(
                "GET",
                PLAYER_URL,
                None,
                {"Authorization": "Bearer " + self.state.get("access_token", "")},
            )
        if status == 429:
            self._retry_after = now + _retry_after(headers)
            return _view(True, "Spotify rate limit. Controls wait for Retry-After.", None, rate_limited=True)
        if status == 204:
            # No player at all. This is the idle card, not a paused track.
            return _view(True, "Nothing playing", None, paused=True)
        if status == 404:
            return _view(True, "No active Spotify device. Open Spotify on a phone or computer.", None, no_device=True)
        if status == 403:
            message = _error_message(status, body) or "Spotify refused playback."
            premium = "premium" in message.lower()
            if premium:
                message = "Spotify Premium is required for playback controls. " + message
            else:
                message = "Spotify refused this account for this app. In Development Mode, add the listener under Users Management. " + message
            return _view(True, message, None, premium=premium, forbidden=not premium)
        if status != 200 or not isinstance(body, dict):
            return _view(True, _error_message(status, body) or "Spotify player request failed.", None, failed=True)
        return _from_player(body)

    def command(self, action: str, now: float | None = None) -> tuple[int, str]:
        """Send one official player command. Pause only while playing, play only while paused."""
        now = time.time() if now is None else now
        current = self.poll(now)
        if not current.get("connected") or current.get("rateLimited") or current.get("noDevice") or current.get("premiumRequired") or current.get("forbidden"):
            return 409, current.get("detail") or "unavailable"
        method, url, body = _command_request(action, current)
        if method is None:
            return 409, "That control is not available in this state."
        status, payload, headers = self.transport.request(
            method,
            url,
            body,
            {"Authorization": "Bearer " + self.state.get("access_token", ""), "Content-Type": "application/json"},
        )
        if status == 429:
            self._retry_after = now + _retry_after(headers)
            return 429, "rate_limited"
        if status == 404:
            return 404, "no_active_device"
        if status == 403:
            message = _error_message(status, payload) or "Spotify refused the command."
            if "premium" in message.lower():
                message = "Spotify Premium is required for playback controls. " + message
            else:
                message = "Spotify refused this account for this app. " + message
            return 403, message
        if status not in (200, 202, 204):
            return status, _error_message(status, payload) or "player_command_failed"
        return 200, "ok"

    def _refresh_if_needed(self, now: float) -> None:
        if self.state.get("expires_at", 0) - now < 60:
            self._refresh(now, force=False)

    def _refresh(self, now: float, force: bool) -> None:
        refresh = self.state.get("refresh_token")
        if not refresh:
            self.state.pop("access_token", None)
            return
        if not force and self.state.get("expires_at", 0) - now >= 60:
            return
        status, body, _headers = self.transport.request("POST", TOKEN_URL, form({
            "grant_type": "refresh_token",
            "refresh_token": refresh,
            "client_id": self.client_id,
        }), None)
        if status != 200 or not isinstance(body, dict):
            if isinstance(body, dict) and body.get("error") == "invalid_grant":
                self.logout()
            return
        if "refresh_token" not in body:
            body["refresh_token"] = refresh
        self._store_token(body, now)

    def _store_token(self, body: dict, now: float) -> None:
        display_name = self.state.get("display_name", "")
        self.state = {
            "access_token": body.get("access_token", ""),
            "refresh_token": body.get("refresh_token", self.state.get("refresh_token", "")),
            "expires_at": now + int(body.get("expires_in") or 3600),
            "scope": body.get("scope", SCOPES),
        }
        if display_name:
            self.state["display_name"] = display_name
        self.token_path.parent.mkdir(parents=True, exist_ok=True)
        self.token_path.write_text(json.dumps(self.state), encoding="utf-8")
        try:
            self.token_path.chmod(0o600)
        except OSError:
            pass


def _from_player(body: dict) -> dict:
    """One player payload. Pause keeps the track so the card can stay on that cover.

    Play and pause commands do not change: pause is offered only while
    is_playing is true, and play only while it is false. An empty item is idle.
    """
    playing = bool(body.get("is_playing"))
    item = body.get("item") if isinstance(body.get("item"), dict) else None
    track = _track(item, body.get("progress_ms"))
    if not playing:
        return _view(True, "Paused", track, paused=True, shuffle=_shuffle(body), repeat=_repeat(body))
    return _view(
        True,
        "Playing",
        track,
        playing=True,
        shuffle=_shuffle(body),
        repeat=_repeat(body),
    )


def _track(item: dict | None, progress_ms: object) -> dict | None:
    """Title, artist, and a Spotify-hosted cover. Other image hosts are dropped."""
    if not item:
        return None
    names = [artist.get("name") for artist in item.get("artists") or [] if isinstance(artist, dict) and artist.get("name")]
    art = None
    images = ((item.get("album") or {}).get("images") or []) if isinstance(item.get("album"), dict) else []
    if images and isinstance(images[0], dict):
        url = images[0].get("url") or ""
        if url.startswith(ART_PREFIXES):
            art = url
    return {
        "title": item.get("name"),
        "artist": ", ".join(names),
        "art": art,
        "position": _seconds(progress_ms),
        "duration": _seconds(item.get("duration_ms")),
    }


def _command_request(action: str, current: dict) -> tuple[str | None, str, dict | None]:
    if action == "pause" and current.get("canPause"):
        return "PUT", PLAYER_URL + "/pause", {}
    if action == "play" and current.get("canPlay"):
        return "PUT", PLAYER_URL + "/play", {}
    if action == "next" and current.get("canSkip"):
        return "POST", PLAYER_URL + "/next", None
    if action == "previous" and current.get("canSkip"):
        return "POST", PLAYER_URL + "/previous", None
    if action == "shuffle" and current.get("canShuffle"):
        state = "false" if current.get("shuffle") == "on" else "true"
        return "PUT", PLAYER_URL + "/shuffle?state=" + state, {}
    if action == "repeat" and current.get("canRepeat"):
        order = {"off": "context", "all": "track", "one": "off"}
        spotify_state = order.get(current.get("repeat") or "off", "off")
        return "PUT", PLAYER_URL + "/repeat?state=" + spotify_state, {}
    return None, "", None


def _view(connected: bool, detail: str, track: dict | None, playing: bool = False, paused: bool = False, shuffle: str = "off", repeat: str = "off", rate_limited: bool = False, no_device: bool = False, premium: bool = False, forbidden: bool = False, failed: bool = False) -> dict:
    controls = connected and playing and not rate_limited and not no_device and not premium and not forbidden and not failed
    paused_controls = connected and paused and not rate_limited and not no_device and not premium and not forbidden and not failed
    # A paused track still has a cover. Idle (no item) does not.
    show = track if (playing or paused) and _track_visible(track) else None
    if paused and show is None:
        detail = "Nothing playing"
    return {
        "connected": connected,
        "demo": False,
        "playing": playing,
        "paused": paused,
        "title": None if show is None else show.get("title"),
        "artist": None if show is None else show.get("artist"),
        "art": None if show is None else show.get("art"),
        "position": None if show is None else show.get("position"),
        "duration": None if show is None else show.get("duration"),
        "shuffle": shuffle,
        "repeat": repeat,
        "controlsEnabled": controls or paused_controls,
        "canPause": controls,
        "canPlay": paused_controls,
        "canSkip": controls or paused_controls,
        "canShuffle": controls or paused_controls,
        "canRepeat": controls or paused_controls,
        "detail": detail,
        "rateLimited": rate_limited,
        "noDevice": no_device,
        "premiumRequired": premium,
        "album": None,
    }


def _track_visible(track: dict | None) -> bool:
    if not track:
        return False
    return bool(track.get("title") or track.get("artist") or track.get("art"))


def _shuffle(body: dict) -> str:
    return "on" if body.get("shuffle_state") else "off"


def _repeat(body: dict) -> str:
    value = body.get("repeat_state")
    if value == "track":
        return "one"
    if value == "context":
        return "all"
    return "off"


def _seconds(value: object) -> int | None:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None
    return int(value // 1000)


def _retry_after(headers: dict) -> float:
    raw = headers.get("Retry-After") or headers.get("retry-after") or "1"
    try:
        seconds = float(raw)
    except (TypeError, ValueError):
        seconds = 1
    return min(300, max(1, seconds))


def _error_message(status: int, body: object) -> str:
    if isinstance(body, dict):
        error = body.get("error")
        if isinstance(error, dict) and error.get("message"):
            return str(error["message"])
        if isinstance(error, str):
            return error
    return f"HTTP {status}"


def form(payload: dict) -> bytes:
    return urllib.parse.urlencode(payload).encode("ascii")


def _load(path: Path) -> dict:
    if not path.exists():
        return {}
    try:
        loaded = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {}
    return loaded if isinstance(loaded, dict) else {}


class UrllibTransport:
    def request(self, method: str, url: str, body: bytes | None, headers: dict | None):
        request = urllib.request.Request(url, data=body, headers=headers or {}, method=method)
        try:
            with urllib.request.urlopen(request, timeout=5) as response:
                raw = response.read()
                parsed = json.loads(raw.decode("utf-8")) if raw else {}
                return response.status, parsed, dict(response.headers)
        except urllib.error.HTTPError as exc:
            raw = exc.read()
            try:
                parsed = json.loads(raw.decode("utf-8")) if raw else {}
            except json.JSONDecodeError:
                parsed = {}
            return exc.code, parsed, dict(exc.headers)
