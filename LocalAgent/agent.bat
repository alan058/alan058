@echo off
rem Starts the local AI agent. Needs Python 3.10+ (python.org) and Ollama (ollama.com).
rem Optional: agent.bat --folder "C:\Users\me\Documents" --model llama3.2
setlocal
cd /d "%~dp0"

set "PY="
where py >nul 2>nul && set "PY=py"
if not defined PY where python >nul 2>nul && set "PY=python"
if not defined PY goto :nopython

%PY% -c "import ollama" 2>nul || %PY% -m pip install -r requirements.txt || goto :nopip

where ollama >nul 2>nul || goto :noollama

rem Download the model the first time (several GB, only once).
set "MODEL=qwen3"
if defined OLLAMA_MODEL set "MODEL=%OLLAMA_MODEL%"
:args
if "%~1"=="" goto :run
if /i "%~1"=="--model" set "MODEL=%~2"
shift
goto :args

:run
%PY% agent.py check --model "%MODEL%" >nul 2>nul || ollama pull "%MODEL%"
%PY% agent.py --model "%MODEL%" %*
pause
exit /b 0

:nopython
echo Python was not found. Install Python 3.10 or newer from python.org and try again.
pause
exit /b 1
:nopip
echo Could not install the ollama package. Check your internet connection.
pause
exit /b 1
:noollama
echo Ollama was not found. Install it from ollama.com, open it once, then try again.
pause
exit /b 1
