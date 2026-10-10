@echo off
rem Sets up NestLaptop so it starts with Windows, even before anyone logs in.
rem It asks for administrator rights.
cd /d "%~dp0"
net session >nul 2>nul || (
    powershell -NoProfile -Command "Start-Process -Verb RunAs -FilePath '%~f0'"
    exit /b
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install_laptop.ps1"
pause
