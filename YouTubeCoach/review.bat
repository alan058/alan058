@echo off
rem Review a YouTube video or channel with Claude. Needs Python 3.10+ from python.org.
rem Usage: drag nothing here; just double-click and paste a link when asked.
setlocal
cd /d "%~dp0"

set "PY="
where py >nul 2>nul && set "PY=py"
if not defined PY where python >nul 2>nul && set "PY=python"
if not defined PY goto :nopython

if not exist config.json (
  echo config.json is missing. Copy config.example.json to config.json and add your keys.
  pause
  exit /b 1
)

%PY% -c "import anthropic, youtube_transcript_api" 2>nul || %PY% -m pip install -r requirements.txt || goto :nopip

echo.
echo Paste a video link to review one video,
echo or a channel @handle / link to review its 5 latest videos.
set /p "TARGET=Link or @handle: "
if "%TARGET%"=="" exit /b 0

echo %TARGET% | findstr /i /r "@ /channel/ ^UC" >nul
if %errorlevel%==0 (
  %PY% video_coach.py channel "%TARGET%"
) else (
  %PY% video_coach.py review "%TARGET%"
)
echo.
echo Reports are in the "reports" folder.
start "" reports
pause
exit /b 0

:nopython
echo Python was not found. Install Python 3.10 or newer from python.org and try again.
pause
exit /b 1
:nopip
echo Could not install the required packages. Check your internet connection.
pause
exit /b 1
