@echo off
echo ============================================
echo  PoseGame - Pose Detection Setup
echo ============================================
echo.

cd /d "%~dp0..\pose_detection"

:: Check Python
python --version >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Python not found. Install Python 3.10+ and add to PATH.
    pause
    exit /b 1
)
echo [OK] Python found
python --version

:: Check ffmpeg
ffmpeg -version >nul 2>&1
if errorlevel 1 (
    echo [WARNING] ffmpeg not found in PATH. Video recording will not work.
    echo          Install via: winget install Gyan.FFmpeg
) else (
    echo [OK] ffmpeg found
)
echo.

:: Create venv
if not exist "venv" (
    echo [1/3] Creating virtual environment...
    python -m venv venv
) else (
    echo [1/3] Virtual environment already exists
)

:: Activate and install
echo [2/3] Installing dependencies...
call venv\Scripts\activate.bat
pip install --upgrade pip >nul 2>&1

echo.
echo [2a] Installing PyTorch with CUDA...
pip install torch torchvision --index-url https://download.pytorch.org/whl/cu129
if errorlevel 1 (
    echo [WARNING] CUDA install failed. Trying CPU-only PyTorch...
    pip install torch torchvision
)

echo.
echo [2b] Installing remaining dependencies...
pip install -r requirements.txt

echo.
echo [3/3] Downloading YOLO pose model (first run only)...
python -c "from ultralytics import YOLO; YOLO('yolo11n-pose.pt')"

echo.
echo ============================================
echo  Setup complete!
echo  Run 'scripts\run.bat' to start.
echo ============================================
pause
