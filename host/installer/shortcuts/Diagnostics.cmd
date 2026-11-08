@echo off
REM Read-only check. Does not install PawnIO, change Fan Control, or open a port.
cd /d "%~dp0"
echo Xeneon HW Monitor Host diagnostics
diego-telemetry-host.exe check-backend
echo.
echo Exit code %ERRORLEVEL%
pause
