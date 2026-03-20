@echo off
echo ============================================
echo  PoseGame - Pose Detection
echo ============================================
echo.

cd /d "%~dp0..\pose_detection"

:: Check venv exists
if not exist "venv\Scripts\activate.bat" (
    echo [ERROR] Virtual environment not found. Run scripts\install.bat first.
    pause
    exit /b 1
)

:: Activate venv
call venv\Scripts\activate.bat

:: Parse arguments or use defaults
if "%1"=="" (
    echo Running with live preview. Press Q in the window to stop.
    echo.
    echo Options:
    echo   run.bat                     - Live preview only
    echo   run.bat --record output.mp4 - Preview + save to file
    echo   run.bat --headless out.mp4  - No preview, save to file only
    echo.
    python main.py
) else (
    python main.py %*
)

pause
