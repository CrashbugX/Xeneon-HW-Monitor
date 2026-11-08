# Telemetry transport

`GET /v1/telemetry` returns one document matching `../schemas/telemetry-snapshot-1.0.0.schema.json`.

The Windows process listens only when `serve` or the Windows service is started with `--listen`, `--token-file`, and, for every-interface binds, `--allow-lan`. Snapshot and watch do not open a port. The token is a file outside this repository and is sent as `Authorization: Bearer`. A `token` query parameter is rejected so it does not land in logs.

The Pi page never calls that port. `client/xeneon/bridge.py` polls it and serves `http://127.0.0.1:<localPort>/api/view` with no token in the JSON.

HTTP on the LAN is not encrypted. Use a private network. This build does not create certificates and does not change the Windows firewall.
