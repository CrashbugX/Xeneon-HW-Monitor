; Xeneon HW Monitor Host installer.
; Copies a self-contained win-x64 build. Does not install PawnIO, does not
; edit Fan Control, and does not change the firewall or Windows security.
; The service section is unchecked. This file never passes a shutdown config.
; Uninstall uses sc stop, which is SERVICE_CONTROL_STOP, not preshutdown,
; and it leaves the ProgramData directory in place.

; 64-bit setup stub so a 64-bit-only Wine prefix can launch it. The payload is win-x64.
Target amd64-unicode

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "Sections.nsh"
!include "x64.nsh"

!define PRODUCT_NAME "Xeneon HW Monitor Host"
!define PRODUCT_VERSION "0.1.0"
!define PRODUCT_PUBLISHER "Xeneon"
!define APP_GUID "{7C2E9A10-4B6D-4E2A-8F31-0A6B5C4D3E21}"
!ifndef PAYLOAD
  !error "Build with makensis -DPAYLOAD=... XeneonHost.nsi"
!endif

Name "${PRODUCT_NAME}"
OutFile "XeneonHwMonitorHost-${PRODUCT_VERSION}-win-x64.exe"
InstallDir "$PROGRAMFILES64\Xeneon HW Monitor Host"
InstallDirRegKey HKLM "Software\Xeneon\HW Monitor Host" "InstallDir"
RequestExecutionLevel admin
Unicode true
SetCompressor /SOLID lzma

VIProductVersion "0.1.0.0"
VIAddVersionKey "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey "CompanyName" "${PRODUCT_PUBLISHER}"
VIAddVersionKey "FileVersion" "0.1.0.0"
VIAddVersionKey "FileDescription" "${PRODUCT_NAME} setup"
VIAddVersionKey "LegalCopyright" "Xeneon"
VIAddVersionKey "ProductVersion" "${PRODUCT_VERSION}"

Var DataDir
Var KeepService

!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Function SetDataDir
  ReadEnvStr $DataDir PROGRAMDATA
  StrCpy $DataDir "$DataDir\Xeneon"
  StrCpy $DataDir "$DataDir\HW Monitor Host"
FunctionEnd

Section "Xeneon HW Monitor Host" SEC_MAIN
  SectionIn RO
  Call SetDataDir
  SetOutPath "$INSTDIR"
  File /r "${PAYLOAD}\*.*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  WriteRegStr HKLM "Software\Xeneon\HW Monitor Host" "InstallDir" "$INSTDIR"
  WriteRegStr HKLM "Software\Xeneon\HW Monitor Host" "Version" "${PRODUCT_VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_GUID}" "DisplayName" "${PRODUCT_NAME}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_GUID}" "DisplayVersion" "${PRODUCT_VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_GUID}" "Publisher" "${PRODUCT_PUBLISHER}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_GUID}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_GUID}" "UninstallString" "$\"$INSTDIR\Uninstall.exe$\""
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_GUID}" "DisplayIcon" "$INSTDIR\diego-telemetry-host.exe"
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_GUID}" "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_GUID}" "NoRepair" 1

  CreateDirectory "$DataDir\config"
  CreateDirectory "$DataDir\logs"
  IfFileExists "$DataDir\config\host.json" host_kept 0
    CopyFiles /SILENT "$INSTDIR\config\example.host.json" "$DataDir\config\host.json"
  host_kept:
  IfFileExists "$DataDir\config\shutdown.example.json" sample_kept 0
    CopyFiles /SILENT "$INSTDIR\config\shutdown.example.json" "$DataDir\config\shutdown.example.json"
  sample_kept:

  CreateDirectory "$SMPROGRAMS\Xeneon HW Monitor Host"
  CreateShortcut "$SMPROGRAMS\Xeneon HW Monitor Host\Diagnostics.lnk" "$INSTDIR\Diagnostics.cmd" "" "$INSTDIR\diego-telemetry-host.exe"
  CreateShortcut "$SMPROGRAMS\Xeneon HW Monitor Host\List sensors.lnk" "$INSTDIR\ListSensors.cmd" "" "$INSTDIR\diego-telemetry-host.exe"
  CreateShortcut "$SMPROGRAMS\Xeneon HW Monitor Host\Snapshot.lnk" "$INSTDIR\Snapshot.cmd" "" "$INSTDIR\diego-telemetry-host.exe"
  CreateShortcut "$SMPROGRAMS\Xeneon HW Monitor Host\Start telemetry feed.lnk" "$INSTDIR\StartFeed.cmd" "" "$INSTDIR\diego-telemetry-host.exe"
  CreateShortcut "$SMPROGRAMS\Xeneon HW Monitor Host\Configuration.lnk" "$WINDIR\explorer.exe" "$DataDir\config"
  CreateShortcut "$SMPROGRAMS\Xeneon HW Monitor Host\Logs.lnk" "$WINDIR\explorer.exe" "$DataDir\logs"
  CreateShortcut "$SMPROGRAMS\Xeneon HW Monitor Host\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
SectionEnd

Section /o "Windows service, manual start, Pi poweroff not armed" SEC_SVC
  Call SetDataDir
  FileOpen $1 "$DataDir\service.opted-in" w
  FileWrite $1 "opt-in$\r$\n"
  FileClose $1
  nsExec::ExecToLog 'sc.exe stop XeneonTelemetryHost'
  nsExec::ExecToLog 'sc.exe delete XeneonTelemetryHost'
  nsExec::ExecToLog 'sc.exe create XeneonTelemetryHost binPath= "$INSTDIR\diego-telemetry-host.exe service" start= demand DisplayName= "Xeneon HW Monitor Host"'
  nsExec::ExecToLog 'sc.exe description XeneonTelemetryHost "Read-only telemetry host. Pi poweroff is a separate opt-in."'
SectionEnd

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_OK|MB_ICONSTOP "This build is 64-bit only."
    Abort
  ${EndIf}
  Call SetDataDir
  StrCpy $KeepService "0"
  IfFileExists "$DataDir\service.opted-in" 0 no_marker
    StrCpy $KeepService "1"
  no_marker:
  ReadRegStr $0 HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_GUID}" "UninstallString"
  ${If} $0 != ""
    ExecWait '$0 /S _?=$INSTDIR'
  ${EndIf}
  ${If} $KeepService == "1"
    SectionSetFlags ${SEC_SVC} ${SF_SELECTED}
  ${EndIf}
FunctionEnd

Section "un.Full"
  Call un.SetDataDir
  nsExec::ExecToLog 'sc.exe stop XeneonTelemetryHost'
  Sleep 500
  nsExec::ExecToLog 'sc.exe delete XeneonTelemetryHost'
  Delete "$SMPROGRAMS\Xeneon HW Monitor Host\Diagnostics.lnk"
  Delete "$SMPROGRAMS\Xeneon HW Monitor Host\List sensors.lnk"
  Delete "$SMPROGRAMS\Xeneon HW Monitor Host\Snapshot.lnk"
  Delete "$SMPROGRAMS\Xeneon HW Monitor Host\Start telemetry feed.lnk"
  Delete "$SMPROGRAMS\Xeneon HW Monitor Host\Configuration.lnk"
  Delete "$SMPROGRAMS\Xeneon HW Monitor Host\Logs.lnk"
  Delete "$SMPROGRAMS\Xeneon HW Monitor Host\Uninstall.lnk"
  RMDir "$SMPROGRAMS\Xeneon HW Monitor Host"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_GUID}"
  DeleteRegKey HKLM "Software\Xeneon\HW Monitor Host"
  ${IfNot} ${Silent}
    MessageBox MB_OK "Removed the program files. Configuration and tokens under ProgramData\Xeneon were left in place."
  ${EndIf}
SectionEnd

Function un.SetDataDir
  ReadEnvStr $DataDir PROGRAMDATA
  StrCpy $DataDir "$DataDir\Xeneon"
  StrCpy $DataDir "$DataDir\HW Monitor Host"
FunctionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_MAIN} "Self-contained host, shortcuts, and example config. Does not start a service or open a port."
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_SVC} "Optional. Registers a manual service with no Pi shutdown config and does not start it."
!insertmacro MUI_FUNCTION_DESCRIPTION_END
