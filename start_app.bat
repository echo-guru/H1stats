@echo off
setlocal
cd /d "%~dp0"

echo.
echo  H1Stats - Starting application...
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
echo.
timeout /t 5 >nul
