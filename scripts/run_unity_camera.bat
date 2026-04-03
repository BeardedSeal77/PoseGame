@echo off
echo ============================================
echo  PoseGame - Unity Camera Stream
echo ============================================
echo.

cd /d "%~dp0..\pose_detection"

set "PYTHON_CMD="

if exist "venv\Scripts\python.exe" (
    set "PYTHON_CMD=venv\Scripts\python.exe"
) else (
    python --version >nul 2>&1
    if not errorlevel 1 (
        set "PYTHON_CMD=python"
    ) else (
        py -3 --version >nul 2>&1
        if not errorlevel 1 (
            set "PYTHON_CMD=py -3"
        )
    )
)

if "%PYTHON_CMD%"=="" (
    echo [ERROR] Python not found. Install Python 3.10+ and add it to PATH, or create pose_detection\venv.
    pause
    exit /b 1
)

echo Running stream_to_unity.py. Press Q in the preview window to stop.
echo.
%PYTHON_CMD% stream_to_unity.py %*

pause