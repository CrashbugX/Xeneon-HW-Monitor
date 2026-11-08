"""Spotify PKCE, paused playback, discovery packets, and setup CSRF."""

from __future__ import annotations

import json
import threading
import unittest
import urllib.error
import urllib.request
from pathlib import Path
from tempfile import TemporaryDirectory

from xeneon.bridge import Bridge
from xeneon.discover import parse_packet, query_packet
from xeneon.session import Session
from xeneon.setupflow import SetupFlow
from xeneon.spotify import PUBLIC_SPOTIFY_CLIENT_ID, SpotifySession, challenge_s256, make_verifier, resolve_client_id


class ScriptedTransport:
    def __init__(self, replies):
        self.replies = list(replies)
        self.calls = []

    def request(self, method, url, body, headers):
        self.calls.append((method, url, body, headers))
        status, payload, response_headers = self.replies.pop(0)
        return status, payload, response_headers


class SpotifyTests(unittest.TestCase):
    def test_blank_client_id_uses_the_public_app_and_keeps_a_custom_one(self):
        self.assertEqual(PUBLIC_SPOTIFY_CLIENT_ID, resolve_client_id(""))
        self.assertEqual(PUBLIC_SPOTIFY_CLIENT_ID, resolve_client_id(None))
        self.assertEqual("custom-public-id", resolve_client_id(" custom-public-id "))
        with TemporaryDirectory() as tmp:
            root = Path(tmp)
            missing = SetupFlow(root / "missing.json", root / "host.token")
            self.assertEqual(PUBLIC_SPOTIFY_CLIENT_ID, missing.client_id)
            blank = root / "blank.json"
            blank.write_text('{"spotifyClientId": "", "hostUrl": "http://127.0.0.1:8765"}\n', encoding="utf-8")
            upgraded = SetupFlow(blank, root / "host.token")
            self.assertEqual(PUBLIC_SPOTIFY_CLIENT_ID, upgraded.client_id)
            saved = json.loads(blank.read_text(encoding="utf-8"))
            self.assertEqual(PUBLIC_SPOTIFY_CLIENT_ID, saved["spotifyClientId"])
            self.assertEqual("http://127.0.0.1:8765", saved["hostUrl"])
            custom = root / "custom.json"
            custom.write_text('{"spotifyClientId": "someone-elses-public-id"}\n', encoding="utf-8")
            kept = SetupFlow(custom, root / "host.token")
            self.assertEqual("someone-elses-public-id", kept.client_id)
            self.assertIn("someone-elses-public-id", custom.read_text(encoding="utf-8"))
            self.assertNotIn(PUBLIC_SPOTIFY_CLIENT_ID, custom.read_text(encoding="utf-8"))

    def test_pkce_uses_public_id_when_constructed_with_it(self):
        with TemporaryDirectory() as tmp:
            session = SpotifySession(Path(tmp) / "spotify.json", PUBLIC_SPOTIFY_CLIENT_ID, 8734, ScriptedTransport([]))
            url = session.begin(now=1000)
            self.assertIn("client_id=" + PUBLIC_SPOTIFY_CLIENT_ID, url)
            self.assertIn("http%3A%2F%2F127.0.0.1%3A8734%2Fspotify%2Fcallback", url)
            self.assertNotIn("client_secret", url)
    def test_pkce_challenge_and_state_replay(self):
        verifier = make_verifier()
        self.assertEqual(64, len(verifier))
        self.assertEqual(challenge_s256(verifier), challenge_s256(verifier))
        with TemporaryDirectory() as tmp:
            session = SpotifySession(Path(tmp) / "spotify.json", "public-client", 8734, ScriptedTransport([
                (200, {"access_token": "a", "refresh_token": "r", "expires_in": 3600}, {}),
                (200, {"access_token": "a2", "refresh_token": "r", "expires_in": 3600}, {}),
            ]))
            url = session.begin(now=1000)
            self.assertIn("code_challenge_method=S256", url)
            self.assertIn("client_id=public-client", url)
            self.assertIn("http%3A%2F%2F127.0.0.1%3A8734%2Fspotify%2Fcallback", url)
            self.assertNotIn("client_secret", url)
            state = session._pending["state"]
            session.finish("code-1", state, now=1001)
            with self.assertRaises(ValueError):
                session.finish("code-1", state, now=1002)
            stored = (Path(tmp) / "spotify.json").read_text(encoding="utf-8")
            self.assertIn("access_token", stored)
            self.assertNotIn("client_secret", stored)

    def test_paused_hides_metadata_and_refresh_error(self):
        with TemporaryDirectory() as tmp:
            path = Path(tmp) / "spotify.json"
            path.write_text(json.dumps({"access_token": "old", "refresh_token": "r", "expires_at": 0}), encoding="utf-8")
            transport = ScriptedTransport([
                (400, {"error": "invalid_grant"}, {}),
            ])
            session = SpotifySession(path, "public-client", 8734, transport)
            view = session.poll(now=10_000)
            self.assertFalse(view["connected"])
            self.assertIsNone(view["title"])

            path.write_text(json.dumps({"access_token": "live", "refresh_token": "r", "expires_at": 10_000 + 3600}), encoding="utf-8")
            session = SpotifySession(path, "public-client", 8734, ScriptedTransport([
                (200, {
                    "is_playing": False,
                    "item": {"name": "Hidden", "artists": [{"name": "Secret"}], "album": {"images": [{"url": "https://i.scdn.co/image/abc"}]}},
                    "progress_ms": 1000,
                    "shuffle_state": False,
                    "repeat_state": "off",
                }, {}),
            ]))
            paused = session.poll(now=10_000)
            self.assertTrue(paused["paused"])
            self.assertIsNone(paused["title"])
            self.assertIsNone(paused["art"])
            self.assertIsNone(paused["position"])
            self.assertEqual("Nothing playing", paused["detail"])
            self.assertTrue(paused["canPlay"])
            self.assertFalse(paused["canPause"])

    def test_pause_play_shuffle_and_repeat_follow_the_player(self):
        playing = {
            "is_playing": True,
            "item": {"name": "Night", "artists": [{"name": "A"}], "duration_ms": 1000, "album": {"images": [{"url": "https://evil.example/a.png"}]}},
            "progress_ms": 100,
            "shuffle_state": True,
            "repeat_state": "off",
        }
        with TemporaryDirectory() as tmp:
            path = Path(tmp) / "spotify.json"
            path.write_text(json.dumps({"access_token": "live", "refresh_token": "r", "expires_at": 50_000}), encoding="utf-8")
            pause = SpotifySession(path, "public-client", 8734, ScriptedTransport([
                (200, playing, {}),
                (200, playing, {}),
                (204, {}, {}),
            ]))
            view = pause.poll(now=1000)
            self.assertTrue(view["canPause"])
            self.assertFalse(view["canPlay"])
            self.assertEqual("Night", view["title"])
            self.assertIsNone(view["art"])
            self.assertEqual((200, "ok"), pause.command("pause", now=1000))
            self.assertIn("/pause", pause.transport.calls[-1][1])

            paused_body = dict(playing)
            paused_body["is_playing"] = False
            play = SpotifySession(path, "public-client", 8734, ScriptedTransport([
                (200, paused_body, {}),
                (200, paused_body, {}),
            ]))
            paused = play.poll(now=1000)
            self.assertIsNone(paused["title"])
            self.assertTrue(paused["canPlay"])
            self.assertFalse(paused["canPause"])
            self.assertEqual((409, "That control is not available in this state."), play.command("pause", now=1000))

            shuffle = SpotifySession(path, "public-client", 8734, ScriptedTransport([
                (200, playing, {}),
                (204, {}, {}),
            ]))
            self.assertEqual((200, "ok"), shuffle.command("shuffle", now=1000))
            self.assertIn("state=false", shuffle.transport.calls[-1][1])

            for current, expected in (("off", "context"), ("context", "track"), ("track", "off")):
                body = dict(playing)
                body["repeat_state"] = current
                session = SpotifySession(path, "public-client", 8734, ScriptedTransport([
                    (200, body, {}),
                    (204, {}, {}),
                ]))
                self.assertEqual((200, "ok"), session.command("repeat", now=1000))
                self.assertIn("state=" + expected, session.transport.calls[-1][1])

    def test_premium_and_account_refusal_are_distinct(self):
        with TemporaryDirectory() as tmp:
            path = Path(tmp) / "spotify.json"
            path.write_text(json.dumps({"access_token": "live", "refresh_token": "r", "expires_at": 50_000}), encoding="utf-8")
            premium = SpotifySession(path, "public-client", 8734, ScriptedTransport([
                (403, {"error": {"status": 403, "message": "Player command failed: Premium required"}}, {}),
            ]))
            refused = premium.poll(now=1000)
            self.assertTrue(refused["premiumRequired"])
            self.assertIsNone(refused["title"])
            self.assertIn("Premium is required", refused["detail"])
            self.assertFalse(refused["canPause"])
            account = SpotifySession(path, "public-client", 8734, ScriptedTransport([
                (403, {"error": {"status": 403, "message": "User not registered in the Developer Dashboard"}}, {}),
            ]))
            blocked = account.poll(now=1000)
            self.assertFalse(blocked["premiumRequired"])
            self.assertIn("Users Management", blocked["detail"])

    def test_rate_limit_and_no_device(self):
        with TemporaryDirectory() as tmp:
            path = Path(tmp) / "spotify.json"
            path.write_text(json.dumps({"access_token": "live", "refresh_token": "r", "expires_at": 9_000}), encoding="utf-8")
            session = SpotifySession(path, "public-client", 8734, ScriptedTransport([
                (429, {"error": {"message": "Too many requests"}}, {"Retry-After": "2"}),
            ]))
            limited = session.poll(now=1000)
            self.assertTrue(limited["rateLimited"])
            self.assertFalse(limited["canPause"])
            session = SpotifySession(path, "public-client", 8734, ScriptedTransport([
                (404, {}, {}),
            ]))
            missing = session.poll(now=1000)
            self.assertTrue(missing["noDevice"])
            self.assertFalse(missing["controlsEnabled"])


class DiscoveryAndCsrfTests(unittest.TestCase):
    def test_announce_without_token_parses(self):
        packet = json.dumps({
            "magic": "xeneon-discover-1",
            "op": "announce",
            "name": "DESKTOP",
            "port": 8765,
            "product": "Xeneon HW Monitor Host",
            "version": "0.2.0",
            "pairing": True,
        }).encode("utf-8")
        parsed = parse_packet(packet)
        self.assertEqual(8765, parsed["port"])
        self.assertNotIn("token", packet.decode("utf-8"))
        self.assertEqual("query", json.loads(query_packet())["op"])
        self.assertIsNone(parse_packet(b'{"magic":"xeneon-discover-1","op":"announce","token":"x","port":1,"name":"n"}'))

    def test_post_without_setup_header_is_rejected(self):
        bridge = Bridge(Session("demo"), None, "", 0)
        bridge.port = 8766
        server = bridge.httpd
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        port = server.server_address[1]
        try:
            request = urllib.request.Request(
                f"http://127.0.0.1:{port}/api/demo/spotify",
                data=b'{"action":"pause"}',
                headers={"Content-Type": "application/json"},
            )
            with self.assertRaises(urllib.error.HTTPError) as caught:
                urllib.request.urlopen(request, timeout=2)
            self.assertEqual(403, caught.exception.code)
        finally:
            bridge.stop()


if __name__ == "__main__":
    unittest.main()
