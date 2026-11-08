# Unpublished 0.2.0

Nothing here has been uploaded. Reassemble the five parts, check the full hash, then install over 0.1.1.

Full installer: 35,352,648 bytes, SHA-256 `d34e5e4695f2a9b33761a927215fbe94acbc54fa7acba95636fd41319b12c972`. Unsigned. PE checksum `0x021C3D53`.

```sh
cat \
  XeneonHwMonitorHost-0.2.0-win-x64.part001 \
  XeneonHwMonitorHost-0.2.0-win-x64.part002 \
  XeneonHwMonitorHost-0.2.0-win-x64.part003 \
  XeneonHwMonitorHost-0.2.0-win-x64.part004 \
  XeneonHwMonitorHost-0.2.0-win-x64.part005 \
  > XeneonHwMonitorHost-0.2.0-win-x64.exe
sha256sum XeneonHwMonitorHost-0.2.0-win-x64.exe
```

| Part | Bytes | SHA-256 |
| --- | ---: | --- |
| `XeneonHwMonitorHost-0.2.0-win-x64.part001` | 8000000 | `46637621032734cce3046e8b47678370b653b7fb5e61b8ee21f6784879afa396` |
| `XeneonHwMonitorHost-0.2.0-win-x64.part002` | 8000000 | `3897bbad8d6e020b22be103db912af85f702eef00d7647015f7efd79982b7cf1` |
| `XeneonHwMonitorHost-0.2.0-win-x64.part003` | 8000000 | `d1f1587da3a8a37dda6b5557283aa8014053d0cb4b4d84c340bc45b5138b4b4d` |
| `XeneonHwMonitorHost-0.2.0-win-x64.part004` | 8000000 | `18ecf19fa751e8579a30397ba0ef7a18b5f09ba66354708b4ffa1e1850604113` |
| `XeneonHwMonitorHost-0.2.0-win-x64.part005` | 3352648 | `d35aa5662e650a8e93cd37cc928987433f76c1590390e876295e2622703faa88` |

## Since the previous unpublished 0.2.0 tree

Portrait layout wraps the demo banner and places the state badge under the temperature. The wide 2560×720 stage is unchanged.

The optional firewall section runs `Repair-PrivateFirewall.ps1`. It reads `publishPort` and `discoveryPort` from `host.json` and adds inbound TCP and UDP rules for the Private profile and the local subnet. It does not open Public and it does not turn the firewall off. Apply port moves the listener and the published URLs. It does not edit the firewall. Run the script yourself, elevated, after a port change. The window does not open the administrator prompt.

Connect Spotify uses the public Client ID `c15bb4b8de984a84a831995690451695`. A fresh install and a config with a missing or blank id use it. A saved nonempty id is kept. Tokens stay in the private Spotify file. The registered redirect is `http://127.0.0.1/spotify/callback`. The bridge sends `http://127.0.0.1:8734/spotify/callback`.

Pi scripts are started with `sh`, because a browser upload does not keep the executable bit. `sh client/scripts/restart-bridge.sh` stops only a process whose command line is this app, then restarts the user service.

## Pi

```sh
sh client/scripts/install.sh
sh client/scripts/restart-bridge.sh
sh client/scripts/update-from-github.sh
```
