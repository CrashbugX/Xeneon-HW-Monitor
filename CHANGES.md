# Upload manifest

Source zip root is the repository root. The setup program is not inside that zip.

The finished installer is unsigned. Its PE checksum is now the normal CheckSumMappedFile value (`0x01A66FA2`). `osslsigncode verify` no longer reports an invalid checksum. It still reports no signature.

The single 27,644,716-byte file is too large for the artifact copy, so it is stored only as raw parts of 8,000,000 bytes or less. Concatenate in numeric order, then check the full SHA-256.

```sh
cat \
  XeneonHwMonitorHost-0.1.0-win-x64.part001 \
  XeneonHwMonitorHost-0.1.0-win-x64.part002 \
  XeneonHwMonitorHost-0.1.0-win-x64.part003 \
  XeneonHwMonitorHost-0.1.0-win-x64.part004 \
  > XeneonHwMonitorHost-0.1.0-win-x64.exe
sha256sum XeneonHwMonitorHost-0.1.0-win-x64.exe
```

Expected full file: 27,644,716 bytes, SHA-256 `b050fbfa8c08f28f6ca3901912e1f18703bd228297f76cb169c0bec026a5e2da`.

| Part | Bytes | SHA-256 |
| --- | --- | --- |
| `XeneonHwMonitorHost-0.1.0-win-x64.part001` | 8000000 | `2475443e47b619ff26c60e92d39708bd58699dc6e2577f9721bbb805e67104bd` |
| `XeneonHwMonitorHost-0.1.0-win-x64.part002` | 8000000 | `24807f48f678b3d489a002aa0ee12370ecdf3d2da791905c9396fc68e97005d4` |
| `XeneonHwMonitorHost-0.1.0-win-x64.part003` | 8000000 | `1f532b63ace684e8bf9e536c251f652f64c76f4bfee0583560b968b5ba1d69eb` |
| `XeneonHwMonitorHost-0.1.0-win-x64.part004` | 3644716 | `57885a495991fa0d0e7588cd9e061f738b841f87b1014f075b905a23e54a6bf4` |

`host/installer/build-installer.sh` now runs `fix-pe-checksum.py` after NSIS. Rebuilds must do that before publishing the exe.
