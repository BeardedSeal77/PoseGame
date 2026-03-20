@echo off
setlocal

echo ============================================================
echo Starting Nimbus Hub - Next.js + Flask
echo ============================================================
echo.

REM Get the directory where this script is located
set SCRIPT_DIR=%~dp0

REM Navigate to the root directory (one level up from scripts)
cd /d "%SCRIPT_DIR%\.."

echo Installing Node dependencies...
call npm install

echo.
echo Starting Next.js frontend and Flask backend...
echo Frontend: http://localhost:3000
echo Backend:  http://localhost:8000
echo.

call npm run dev

REM Keep window open to see any errors
pause

endlocal

