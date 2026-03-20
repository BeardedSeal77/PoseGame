@echo off
setlocal

echo ============================================================
echo Nimbus Hub Installation
echo ============================================================
echo.

REM Get the directory where this script is located
set SCRIPT_DIR=%~dp0

REM Navigate to the root directory (one level up from scripts)
cd /d "%SCRIPT_DIR%\.."

REM ============================================================
REM 1. Install Node.js dependencies
REM ============================================================
echo [1/2] Installing Node.js dependencies...
call npm install
if errorlevel 1 (
    echo ERROR: npm install failed
    pause
    exit /b 1
)
echo ✓ Node.js dependencies installed
echo.

REM ============================================================
REM 2. Install Python dependencies
REM ============================================================
echo [2/2] Installing Python dependencies...

REM Use Python 3.10.9 specifically
set PYTHON_PATH=C:\Users\QXZ6NXH\AppData\Local\Programs\Python\Python310\python.exe

REM Create virtual environment in root .venv
if not exist ".venv" (
  echo Creating Python virtual environment...
  "%PYTHON_PATH%" -m venv ".venv"
)

REM Activate the virtual environment
call ".venv\Scripts\activate.bat"

REM Upgrade pip and install build tools
python -m pip install --upgrade pip setuptools wheel

REM Clear pip cache completely
echo Clearing pip cache...
pip cache purge

REM Install core dependencies one by one to avoid cache issues
echo Installing Flask...
pip install --no-cache-dir flask==3.0.0

echo Installing Flask-CORS...
pip install --no-cache-dir flask-cors==4.0.0

echo Installing pydantic...
pip install --no-cache-dir pydantic

echo Installing requests...
pip install --no-cache-dir requests==2.32.3

echo Installing numpy...
pip install --no-cache-dir numpy==1.24.4

echo Installing OpenCV...
pip install --no-cache-dir opencv-python==4.9.0.80

echo Installing ultralytics (YOLO)...
pip install --no-cache-dir ultralytics

echo ✓ Python dependencies installed
echo.
echo ============================================================
echo Installation Complete!
echo ============================================================
echo.
echo To start the system, run:
echo   scripts\run.bat
echo   OR
echo   npm run dev
echo.
echo Optional: To add GPU support for YOLO, run:
echo   pip install torch torchvision --index-url https://download.pytorch.org/whl/cu128
echo.

REM Pause to see any errors
pause

endlocal

