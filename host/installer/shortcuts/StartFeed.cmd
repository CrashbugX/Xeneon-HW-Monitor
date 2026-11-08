@echo off
REM Explicit opt-in feed. Refuses to listen until serve.on exists.
REM serve.lan is a second opt-in for every interface. No firewall rule is added.
cd /d "%~dp0"
set "DATA=%ProgramData%\Xeneon\HW Monitor Host"
set "TOKEN=%DATA%\telemetry.token"
set "CFG=%DATA%\config\host.json"
if not exist "%TOKEN%" (
  echo Create this file with one line, mode limited to your user:
  echo   %TOKEN%
  echo The installer does not create a token.
  pause
  exit /b 2
)
if not exist "%DATA%\config\serve.on" (
  echo The feed stays off until you create an empty file:
  echo   %DATA%\config\serve.on
  echo Add %DATA%\config\serve.lan as well if the Pi is on another machine.
  echo This does not add a Windows Firewall exception. See FIREWALL.txt.
  pause
  exit /b 2
)
if exist "%DATA%\config\serve.lan" (
  diego-telemetry-host.exe serve --listen 0.0.0.0 --port 8765 --allow-lan --token-file "%TOKEN%" --config "%CFG%"
) else (
  diego-telemetry-host.exe serve --listen 127.0.0.1 --port 8765 --token-file "%TOKEN%" --config "%CFG%"
)
echo Exit code %ERRORLEVEL%
pause
