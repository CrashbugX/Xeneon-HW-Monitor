# Shutdown channel

This is not the telemetry feed. Dropping telemetry does not call it.

`POST /v1/poweroff` with a JSON object:

- `action` must be `poweroff`. Any other action is refused.
- `nonce` is a string. Repeating it is a replay and does not run the helper again.
- `issuedAtUtc` must be within 180 seconds of the Pi clock.

The header is `Authorization: Bearer` using the shutdown token, which is a different file from the telemetry token.

`GET /v1/poweroff/status` reports `accepted`, `syncing`, `halt-issued`, or `helper-missing`.

`halt-issued` means the Pi finished `sync` and started the fixed helper. The Windows coordinator then waits `settleAfterHaltIssuedSeconds`. A 202 response alone, or the TCP connection closing, is not success.

The helper is `/usr/local/sbin/xeneon-poweroff`. It takes no arguments. The optional sudoers file allows only that path.

Windows preshutdown facts, from Microsoft’s `SERVICE_PRESHUTDOWN_INFO` page: after `SERVICE_CONTROL_PRESHUTDOWN`, the service control manager waits until the service reports `SERVICE_STOPPED` or `dwPreshutdownTimeout` elapses. From Windows 10 build 15063 the default timeout is 10,000 ms. Earlier releases used 180,000 ms. While the service is `SERVICE_STOP_PENDING` it must call `SetServiceStatus` with an increasing `dwCheckPoint` and a `dwWaitHint` before that hint expires, or the SCM can treat it as hung. This program uses a 4,000 ms hint and updates every poll. The configured grace is capped at 90 seconds. When the timer ends, Windows continues shutdown anyway.
