@echo off
REM Opens the configuration directory. Private files live here and survive uninstall.
start "" explorer.exe "%ProgramData%\Xeneon\HW Monitor Host\config"
