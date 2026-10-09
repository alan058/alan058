@echo off
rem Runs ProcWatch from source, fully offline.
rem Needs Python 3.10+ from python.org (keep "tcl/tk and IDLE" ticked in the installer).
rem psutil is installed from the bundled wheels folder, never from the internet.
setlocal
cd /d "%~dp0"

rem Prefer the "py" launcher that python.org installs; fall back to python on PATH.
set "PY="
set "PYW="
where py >nul 2>nul && (set "PY=py" & set "PYW=pyw")
if not defined PY where python >nul 2>nul && (set "PY=python" & set "PYW=pythonw")
if not defined PY goto :nopython
%PY% -c "import sys; sys.exit(sys.version_info < (3, 7))" 2>nul || goto :nopython

%PY% -c "import tkinter" 2>nul || goto :notk
%PY% -c "import psutil" 2>nul || %PY% -m pip install --no-index --find-links wheels psutil || goto :nopsutil

start "" %PYW% ProcWatch.pyw
exit /b 0

:nopython
echo Python was not found. Install Python 3.10 or newer from python.org,
echo then run this file again. No internet is needed after that.
pause
exit /b 1
:notk
echo Your Python is missing Tk. Re-run the python.org installer, choose Modify,
echo and tick "tcl/tk and IDLE".
pause
exit /b 1
:nopsutil
echo Could not install psutil from the wheels folder.
pause
exit /b 1
