@echo off
rem Removes the NestLaptop startup task and firewall rule. It asks for administrator rights.
net session >nul 2>nul || (
    powershell -NoProfile -Command "Start-Process -Verb RunAs -FilePath '%~f0'"
    exit /b
)
powershell -NoProfile -Command "Stop-ScheduledTask -TaskName NestLaptop -ErrorAction SilentlyContinue; Unregister-ScheduledTask -TaskName NestLaptop -Confirm:$false -ErrorAction SilentlyContinue; Remove-NetFirewallRule -DisplayName 'NestLaptop agent' -ErrorAction SilentlyContinue"
echo NestLaptop removed.
pause
