@echo off
rem Builds a single ProcWatch.exe in the dist folder, fully offline.
rem psutil and PyInstaller are installed from the bundled wheels folder, never from the internet.
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
%PY% -m pip install --no-index --find-links wheels --find-links wheels\build psutil pyinstaller || goto :error
%PY% -m PyInstaller --noconfirm --onefile --windowed --name ProcWatch ProcWatch.pyw || goto :error
echo.
echo Done: %~dp0dist\ProcWatch.exe
pause
exit /b 0
:error
echo Build failed.
pause
exit /b 1
