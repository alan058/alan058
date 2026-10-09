@echo off
rem Runs ProcWatch from source. Needs Python 3.10+ from python.org (tick "tcl/tk" during install).
cd /d "%~dp0"
python -c "import psutil" 2>nul || python -m pip install -r requirements.txt
start "" pythonw ProcWatch.pyw
