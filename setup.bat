@echo off
rem One-step setup and build (runs as administrator): installs missing build tools with winget after asking
rem (Enter = yes), creates and trusts the code signing certificate, then builds the portable exe and the installer.
rem Usage: setup.bat [--yes] [build.ps1 options]     setup.bat --help
setlocal

fltmc >nul 2>&1
if errorlevel 1 goto elevate

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\setup\Setup.ps1" %*
set "SETUP_EXIT=%ERRORLEVEL%"
echo.
pause
exit /b %SETUP_EXIT%

:elevate
echo Requesting administrator rights...
if "%~1"=="" (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
) else (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -ArgumentList '%*' -Verb RunAs"
)
if errorlevel 1 (
    echo Administrator rights were not granted; nothing was done.
    pause
    exit /b 1
)
exit /b 0
