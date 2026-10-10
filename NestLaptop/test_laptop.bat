@echo off
rem Runs NestLaptop in this window in "dry run" mode: it shows what Google Home asked for
rem but does not really shut down. Close the window or press Ctrl+C to stop.
rem Stop the installed copy first (uninstall_laptop.bat) if you already installed it.
setlocal
cd /d "%~dp0"
set "PY="
where py >nul 2>nul && set "PY=py"
if not defined PY where python >nul 2>nul && set "PY=python"
if not defined PY (
    echo Python was not found. Install Python 3.10 or newer from python.org first.
    pause
    exit /b 1
)
if not exist lib\sinricpro (
    %PY% -m pip install --upgrade --target lib -r requirements.txt || goto :error
)
%PY% nest_laptop.py laptop --dry-run
pause
exit /b 0
:error
echo Could not install the Sinric Pro library.
pause
exit /b 1
