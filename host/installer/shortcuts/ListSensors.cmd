@echo off
REM One inventory read. Uses host.json if you have edited it. Does not control fans.
cd /d "%~dp0"
set "CFG=%ProgramData%\Xeneon\HW Monitor Host\config\host.json"
if exist "%CFG%" (
  diego-telemetry-host.exe list-sensors --config "%CFG%"
) else (
  diego-telemetry-host.exe list-sensors
)
echo.
pause
