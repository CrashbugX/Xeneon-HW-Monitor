"""UDP discovery for Xeneon hosts.

The packet is the same JSON the Windows host sends. It names the computer
and the telemetry port. It does not contain a token. Networks that drop
broadcasts use the manual address in the setup panel instead.
"""

from __future__ import annotations

import json
import socket

MAGIC = "xeneon-discover-1"
DEFAULT_PORT = 8764


def query_packet() -> bytes:
    return json.dumps({"magic": MAGIC, "op": "query"}, separators=(",", ":")).encode("utf-8")


def parse_packet(blob: bytes) -> dict | None:
    try:
        payload = json.loads(blob.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError):
        return None
    if not isinstance(payload, dict) or payload.get("magic") != MAGIC:
        return None
    if payload.get("op") != "announce":
        return None
    if "token" in json.dumps(payload).lower():
        return None
    port = payload.get("port")
    if not isinstance(port, int):
        return None
    return {
        "name": str(payload.get("name") or "Xeneon host"),
        "port": port,
        "product": str(payload.get("product") or ""),
        "version": str(payload.get("version") or ""),
        "baseUrl": "",
    }


def discover(timeout: float = 0.6, port: int = DEFAULT_PORT) -> list[dict]:
    """Broadcast one query and collect announcements. Failure returns an empty list."""
    found: list[dict] = []
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
        sock.settimeout(timeout)
        sock.sendto(query_packet(), ("255.255.255.255", port))
        while True:
            try:
                blob, address = sock.recvfrom(2048)
            except TimeoutError:
                break
            parsed = parse_packet(blob)
            if parsed is None:
                continue
            parsed["baseUrl"] = f"http://{address[0]}:{parsed['port']}"
            found.append(parsed)
    except OSError:
        return found
    finally:
        sock.close()
    return found
