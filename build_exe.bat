@echo off
rem Builds a single offline ProcWatch.exe in the dist folder.
rem Internet is only needed once, to download psutil and PyInstaller.
cd /d "%~dp0"
python -m pip install -r requirements.txt pyinstaller || goto :error
python -m PyInstaller --noconfirm --onefile --windowed --name ProcWatch ProcWatch.pyw || goto :error
echo.
echo Done: %~dp0dist\ProcWatch.exe
pause
exit /b 0
:error
echo Build failed.
pause
exit /b 1
