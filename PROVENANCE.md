# Provenance

Checked on 8 November 2026. Nothing in this file is a guess about a private Fan Control config. Fan Control's application data was not opened.

## Fan Control

The site <https://getfancontrol.com/> has a button whose HTML href is `https://github.com/Rem0o/FanControl.Releases`. The same page's download control was labeled "Download V285" when fetched that day. The "Open source backend" section links to <https://github.com/LibreHardwareMonitor/LibreHardwareMonitor> and says the backend is mainly based on LibreHardwareMonitor.

`Rem0o/FanControl.Releases` is a release repository. Its README says: "Sources for this software are closed." This host does not treat that repository as the application source and does not copy its zip or its DLLs.

Release V238 (18 September 2025) says Fan Control moved to a PawnIO build of LibreHardwareMonitor and stopped shipping WinRing0. The README still says V238 and above ship that PawnIO build, and that WinRing0 (`FanControl.sys`) in V237 and below was flagged by Defender as `Trojan:Win32/Vigorf.A` as of 9 April 2025.

Release V285 was published 2 November 2026. Its notes say "Update LibreHardwareMonitorLib" and do not name a package version. There is a `FanControl_285_net_10_0.zip` asset. That zip was not downloaded. The embedded library inside Fan Control is therefore not assumed to be NuGet 0.9.6.

## LibreHardwareMonitorLib 0.9.6

Pinned NuGet package: `LibreHardwareMonitorLib` version `[0.9.6]`.

| Fact | Value |
| --- | --- |
| Feed | `https://api.nuget.org/v3/index.json` |
| nupkg URL | `https://api.nuget.org/v3-flatcontainer/librehardwaremonitorlib/0.9.6/librehardwaremonitorlib.0.9.6.nupkg` |
| nupkg size | 7,161,119 bytes |
| nupkg SHA-256 | `60db5e0d9135f4658b385cae139d3b9eaeac0a8e867d322d20b5dbe4c4039197` |
| nupkg SHA-512, base64 | `nwn6J4tEuiQUcBZgxtWHByKSPE6kx9VfQFx36La0TfezjOXJTfljdQjt1jMFWGcNBLX0OHl8rz/owGPNTqJPFA==` |
| NuGet lock content hash | `W4hQ5Ja51/1cEDZnh9kG5UHFBwjjY2+oIpSUhSsgeYXKRKezrkw3sEFrhQkrOuo53xkXyfWV8ZErMqQuGqWIMQ==` |
| nuspec license | MPL-2.0 |
| nuspec repository commit | `3d331e3370efb858411f19511373eff65a218701` |
| Git tag | `v0.9.6`, same commit, published 14 February 2026 |

The SHA-512 above is the hash of the nupkg bytes and matches NuGet's `.nupkg.sha512` sidecar. The lock-file content hash is a different field stored in `.nupkg.metadata`. Both were read from the package NuGet restored.

The package layout is `ref/` plus `runtimes/win-x64`, `win-x86`, and `win-arm64`. There is no Linux runtime asset. The win-x64 `net10.0` implementation DLL is 1,214,976 bytes. Strings from that DLL include `LibreHardwareMonitor.Resources.PawnIo.AMDFamily17.bin`, `RyzenSMU.bin`, `LpcIO.bin`, and the other `PawnIo` module names. A search of the same DLL for `WinRing0` and `Ring0` returned no hits. Fan-control type names exist inside the library. This host does not call them.

NuGet also lists prerelease `0.9.7-pre781`, updated 6 November 2026. It was not used. The stable package is the pin.

WinRing0 was removed from the library in pull request 1857, merged 16 September 2025 as commit `eb5e1a20be996d4865170b13bab97af43d97f341`. That commit deletes `Ring0.cs` and the WinRing0 resources and adds the PawnIO modules. v0.9.6 is after that commit.

`PawnIo.cs` at the v0.9.6 commit:

- `IsInstalled` is true when `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO` `DisplayVersion` parses as a version. It also tries the 64-bit registry view.
- `LoadModuleFromResource` opens `\\?\GLOBALROOT\Device\PawnIO`. If the handle is invalid it returns an unloaded object. It does not start an installer.
- `Execute` returns `new long[outLength]` when the module is not loaded. That is a zero-filled buffer, not an error code.

`Amd17Cpu` reads the family 17h/19h temperature through that module and scales the register into degrees C. A zero buffer becomes 0 C. This host does not publish that 0 C as a temperature. See `host/docs/COLLECTION.md`.

Pull request 2174, merged 20 January 2026 as commit `4b3ed8bb46`, updates `AMDFamily17.bin` and is titled "Update PawnIO modules to 2.2". The pull request points at `https://github.com/namazso/PawnIO.Modules/releases/tag/0.2.2`. The v0.9.6 release notes include that pull request. `LibreHardwareMonitorLib/Resources/PawnIo/README` at the same tag still says the modules belong to the 0.1.6 release. That README's last change on this tag is commit `f5d5d3ee5f` from 1 November 2025, which is older than the binary update. The README text is stale. The module version claimed here is the January 2026 binary update, not the README sentence.

`SensorType` in `ISensor.cs` at this tag comments `Data` as `GB = 2^30 Bytes` and `SmallData` as `MB = 2^20 Bytes`. Later commits on the default branch have changed that comment. This host follows the 0.9.6 comment and multiplies by 2^30 and 2^20. It does not use decimal GB or MB.

## PawnIO driver

The driver is not in the NuGet package. The library only carries signed module binaries.

- Project page: <https://pawnio.eu/>
- Installer repository: <https://github.com/namazso/PawnIO.Setup>
- Latest release checked: tag `2.2.0`, published 15 March 2026, <https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0>

The January module update and the March driver installer are both called 2.2 in their projects. They were not byte-compared. `check-backend` prints the installed driver `DisplayVersion` and does not upgrade it.

The module `COPYING` file inside the LibreHardwareMonitor tree at this tag is the GNU LGPL 2.1. `THIRD-PARTY-NOTICES.txt` repeats that for PawnIO.Modules. The library's own license is MPL-2.0.

## SDK used to produce the recorded build

| Tool | Version |
| --- | --- |
| .NET SDK | 10.0.403 |
| Shared runtime | Microsoft.NETCore.App 10.0.14 |
| Target framework | net10.0 |
| Self-contained publish | `dotnet publish -c Release -r win-x64 --self-contained true` produced a folder of about 83 MB. It was not stored. |
| Stored exe publish | `dotnet publish -c Release -r win-x64 --self-contained false`. Framework `Microsoft.NETCore.App` 10.0.0 with normal roll-forward. |

The support dates are from <https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core>, page last updated 25 October 2026.
