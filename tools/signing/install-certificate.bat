@echo off
rem Trusts the USBIPD Manager code signing certificate on this machine (Local Computer > Trusted Root).
rem Copy this file together with USBIPD-Manager-CodeSigning.cer to the other machine and double-click it.
setlocal
set "CER=%~dp0USBIPD-Manager-CodeSigning.cer"

if not exist "%CER%" (
    echo Certificate not found: %CER%
    pause
    exit /b 1
)

fltmc >nul 2>&1
if errorlevel 1 (
    echo Requesting administrator rights...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b 0
)

certutil -addstore -f Root "%CER%"
if errorlevel 1 (
    echo.
    echo Installing the certificate failed.
    pause
    exit /b 1
)

echo.
echo Done. Apps signed by "Minh Viet" now show a verified publisher on this machine.
pause
exit /b 0
