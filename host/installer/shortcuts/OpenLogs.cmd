@echo off
REM Opens the log and snapshot directory.
if not exist "%ProgramData%\Xeneon\HW Monitor Host\logs" mkdir "%ProgramData%\Xeneon\HW Monitor Host\logs"
start "" explorer.exe "%ProgramData%\Xeneon\HW Monitor Host\logs"
