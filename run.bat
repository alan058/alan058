@echo off
rem Runs ProcWatch from source, fully offline.
rem Needs Python 3.10+ from python.org (keep "tcl/tk and IDLE" ticked in the installer).
rem psutil is installed from the bundled wheels folder, never from the internet.
cd /d "%~dp0"
python -c "import psutil" 2>nul || python -m pip install --no-index --find-links wheels psutil || goto :error
start "" pythonw ProcWatch.pyw
exit /b 0
:error
echo Could not install psutil from the wheels folder.
pause
exit /b 1
