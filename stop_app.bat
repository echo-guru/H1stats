@echo off
setlocal
cd /d "%~dp0"

echo Stopping H1Stats...

taskkill /FI "WINDOWTITLE eq H1Stats Backend*" /F >nul 2>&1
taskkill /FI "WINDOWTITLE eq H1Stats Frontend*" /F >nul 2>&1

for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":5002" ^| findstr "LISTENING"') do (
    taskkill /PID %%p /F >nul 2>&1
)

for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":5180" ^| findstr "LISTENING"') do (
    taskkill /PID %%p /F >nul 2>&1
)

echo Done. Ports 5002 and 5180 should be free.
timeout /t 3 >nul
