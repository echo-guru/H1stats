@echo off
setlocal
cd /d "%~dp0"

echo.
echo  H1Stats - Starting application...
echo.

echo Cleaning up previous H1Stats processes...

taskkill /FI "WINDOWTITLE eq H1Stats Backend*" /F >nul 2>&1
taskkill /FI "WINDOWTITLE eq H1Stats Frontend*" /F >nul 2>&1

for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":5002" ^| findstr "LISTENING"') do (
    taskkill /PID %%p /F >nul 2>&1
)

for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":5180" ^| findstr "LISTENING"') do (
    taskkill /PID %%p /F >nul 2>&1
)

timeout /t 2 /nobreak >nul
echo.

where node >nul 2>&1
if errorlevel 1 (
    echo ERROR: Node.js is not installed or not in PATH.
    echo Install Node.js 18+ from https://nodejs.org/
    pause
    exit /b 1
)

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET 8 SDK is not installed or not in PATH.
    echo Download from https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

if not exist "frontend\node_modules\" (
    echo Installing frontend dependencies...
    pushd frontend
    call npm install
    if errorlevel 1 (
        echo Failed to install frontend dependencies.
        popd
        pause
        exit /b 1
    )
    popd
    echo.
)

start "H1Stats Backend" cmd /k pushd "%~dp0backend" ^&^& dotnet run --project src/H1Stats.Api
start "H1Stats Frontend" cmd /k pushd "%~dp0frontend" ^&^& npm run dev

echo Waiting for servers to start...
timeout /t 6 /nobreak >nul

start "" "http://localhost:5180"

echo.
echo  H1Stats is running.
echo    Frontend: http://localhost:5180
echo    Backend:  http://localhost:5002
echo.
echo  Close the Backend and Frontend windows to stop the app.
echo  Or run stop_app.bat to shut down without restarting.
echo.
timeout /t 5 >nul
