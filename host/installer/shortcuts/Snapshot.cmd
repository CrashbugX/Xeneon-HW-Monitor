@echo off
REM One JSON sample written into the logs folder. Does not open a listener.
cd /d "%~dp0"
set "CFG=%ProgramData%\Xeneon\HW Monitor Host\config\host.json"
set "OUT=%ProgramData%\Xeneon\HW Monitor Host\logs\snapshot.json"
if not exist "%ProgramData%\Xeneon\HW Monitor Host\logs" mkdir "%ProgramData%\Xeneon\HW Monitor Host\logs"
if exist "%CFG%" (
  diego-telemetry-host.exe snapshot --config "%CFG%" --output "%OUT%"
) else (
  diego-telemetry-host.exe snapshot --output "%OUT%"
)
echo Wrote %OUT%
echo Exit code %ERRORLEVEL%
pause
