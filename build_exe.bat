@echo off
rem Builds a single ProcWatch.exe in the dist folder, fully offline.
rem psutil and PyInstaller are installed from the bundled wheels folder, never from the internet.
cd /d "%~dp0"
python -m pip install --no-index --find-links wheels --find-links wheels\build psutil pyinstaller || goto :error
python -m PyInstaller --noconfirm --onefile --windowed --name ProcWatch ProcWatch.pyw || goto :error
echo.
echo Done: %~dp0dist\ProcWatch.exe
pause
exit /b 0
:error
echo Build failed.
pause
exit /b 1
