; Xeneon HW Monitor Host installer.
; Copies a self-contained win-x64 build over an existing install.
; It does not uninstall first, so settings and pairing tokens stay.
; It does not install PawnIO and it does not edit Fan Control.
; Firewall rules and the logon task are applied every time. The optional
; service section stays unchecked and does not arm Pi poweroff.
; Uninstall uses sc stop, which is SERVICE_CONTROL_STOP, not preshutdown,
; and it leaves the ProgramData directory in place.

; 64-bit setup stub so a 64-bit-only Wine prefix can launch it. The payload is win-x64.
Target amd64-unicode

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "Sections.nsh"
!include "x64.nsh"
!include "nsDialogs.nsh"
!include "FileFunc.nsh"

!define PRODUCT_NAME "Xeneon HW Monitor Host"
!define PRODUCT_VERSION "0.3.0"
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

VIProductVersion "0.3.0.0"
VIAddVersionKey "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey "CompanyName" "${PRODUCT_PUBLISHER}"
VIAddVersionKey "FileVersion" "0.3.0.0"
VIAddVersionKey "FileDescription" "${PRODUCT_NAME} setup"
VIAddVersionKey "LegalCopyright" "Xeneon"
VIAddVersionKey "ProductVersion" "${PRODUCT_VERSION}"

Var DataDir
Var KeepService
Var DeleteData
Var DeleteBox

!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
UninstPage custom un.AskData
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
  ; The updater has already exited. This catches a window that is still open.
  nsExec::ExecToLog 'taskkill /IM XeneonHwMonitor.exe /F'
  Sleep 400
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
  CreateShortcut "$SMPROGRAMS\Xeneon HW Monitor Host\Xeneon HW Monitor.lnk" "$INSTDIR\XeneonHwMonitor.exe"
  CreateShortcut "$SMPROGRAMS\Xeneon HW Monitor Host\Diagnostics.lnk" "$INSTDIR\Diagnostics.cmd" "" "$INSTDIR\diego-telemetry-host.exe"
  CreateShortcut "$SMPROGRAMS\Xeneon HW Monitor Host\List sensors.lnk" "$INSTDIR\ListSensors.cmd" "" "$INSTDIR\diego-telemetry-host.exe"
  CreateShortcut "$SMPROGRAMS\Xeneon HW Monitor Host\Snapshot.lnk" "$INSTDIR\Snapshot.cmd" "" "$INSTDIR\diego-telemetry-host.exe"
  CreateShortcut "$SMPROGRAMS\Xeneon HW Monitor Host\Start telemetry feed.lnk" "$INSTDIR\StartFeed.cmd" "" "$INSTDIR\diego-telemetry-host.exe"
  CreateShortcut "$SMPROGRAMS\Xeneon HW Monitor Host\Configuration.lnk" "$WINDIR\explorer.exe" "$DataDir\config"
  CreateShortcut "$SMPROGRAMS\Xeneon HW Monitor Host\Logs.lnk" "$WINDIR\explorer.exe" "$DataDir\logs"
  CreateShortcut "$SMPROGRAMS\Xeneon HW Monitor Host\Uninstall.lnk" "$INSTDIR\Uninstall.exe"

  ; Idempotent. Private and Domain, this exe, local subnet. Public is not opened.
  nsExec::ExecToLog 'powershell -NoProfile -ExecutionPolicy Bypass -File "$INSTDIR\Repair-PrivateFirewall.ps1" -Program "$INSTDIR\XeneonHwMonitor.exe"'
  IfFileExists "$DataDir\config\start-with-windows.off" skip_task
    nsExec::ExecToLog '"$INSTDIR\XeneonHwMonitor.exe" --install-logon-task'
  skip_task:
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
  ; Do not uninstall first. The same product GUID is overwritten in place.
  ${If} $KeepService == "1"
    SectionSetFlags ${SEC_SVC} ${SF_SELECTED}
  ${EndIf}
FunctionEnd

Function .onInstSuccess
  IfSilent 0 not_silent
    Exec '"$INSTDIR\XeneonHwMonitor.exe"'
  not_silent:
FunctionEnd

Section "un.Full"
  Call un.SetDataDir
  nsExec::ExecToLog 'taskkill /IM XeneonHwMonitor.exe /F'
  nsExec::ExecToLog 'schtasks /Delete /F /TN "Xeneon HW Monitor Host"'
  nsExec::ExecToLog 'powershell -NoProfile -ExecutionPolicy Bypass -File "$INSTDIR\Repair-PrivateFirewall.ps1" -RemoveOnly'
  nsExec::ExecToLog 'sc.exe stop XeneonTelemetryHost'
  Sleep 500
  nsExec::ExecToLog 'sc.exe delete XeneonTelemetryHost'
  nsExec::ExecToLog 'netsh advfirewall firewall delete rule name="Xeneon HW Monitor Host"'
  nsExec::ExecToLog 'netsh advfirewall firewall delete rule name="Xeneon HW Monitor Host TCP"'
  nsExec::ExecToLog 'netsh advfirewall firewall delete rule name="Xeneon HW Monitor Host UDP"'
  Delete "$SMPROGRAMS\Xeneon HW Monitor Host\Xeneon HW Monitor.lnk"
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
  ${If} $DeleteData == "1"
    RMDir /r "$DataDir"
  ${EndIf}
  ${IfNot} ${Silent}
    ${If} $DeleteData == "1"
      MessageBox MB_OK "Removed the program files and this app's configuration folder."
    ${Else}
      MessageBox MB_OK "Removed the program files. Configuration and tokens under ProgramData\Xeneon\HW Monitor Host were left in place."
    ${EndIf}
  ${EndIf}
SectionEnd

Function un.onInit
  StrCpy $DeleteData "0"
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/DELETEDATA" $1
  IfErrors skip_delete
    StrCpy $DeleteData "1"
  skip_delete:
FunctionEnd

Function un.AskData
  ${If} ${Silent}
    Abort
  ${EndIf}
  nsDialogs::Create 1018
  Pop $0
  ${NSD_CreateLabel} 0 0 100% 36u "Program files will be removed. Configuration and tokens under ProgramData\Xeneon\HW Monitor Host are kept unless you check the box. Your user profile is not deleted."
  Pop $2
  ${NSD_CreateCheckbox} 0 48u 100% 12u "Also delete this app's configuration and tokens. This cannot be undone."
  Pop $DeleteBox
  nsDialogs::Show
  ${NSD_GetState} $DeleteBox $0
  ${If} $0 == ${BST_CHECKED}
    StrCpy $DeleteData "1"
  ${EndIf}
FunctionEnd

Function un.SetDataDir
  ReadEnvStr $DataDir PROGRAMDATA
  StrCpy $DataDir "$DataDir\Xeneon"
  StrCpy $DataDir "$DataDir\HW Monitor Host"
FunctionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_MAIN} "Self-contained host, shortcuts, firewall rules, and the logon task. Does not start the optional service."
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_SVC} "Optional. Registers a manual service with no Pi shutdown config and does not start it."
!insertmacro MUI_FUNCTION_DESCRIPTION_END
