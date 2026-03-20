@echo off
REM Launch Webots with proxy configuration

REM Set proxy environment variables
set HTTP_PROXY=http://proxy.w9:8080
set HTTPS_PROXY=http://proxy.w9:8080
set http_proxy=http://proxy.w9:8080
set https_proxy=http://proxy.w9:8080

REM Optional: Bypass proxy for localhost
set NO_PROXY=localhost,127.0.0.1

REM Launch Webots (adjust path if needed)
REM Default Webots installation path:
"C:\Program Files\Webots\msys64\mingw64\bin\webots.exe"

REM Alternative paths (uncomment if needed):
REM "C:\Program Files (x86)\Webots\msys64\mingw64\bin\webots.exe"
REM "%LOCALAPPDATA%\Programs\Webots\msys64\mingw64\bin\webots.exe"
