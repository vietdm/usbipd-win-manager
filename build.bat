@echo off
rem Double-click friendly wrapper for build.ps1; accepts the same arguments (build.bat --help).
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
set "BUILD_EXIT=%ERRORLEVEL%"

rem Pause only when Explorer started this window (cmd /c with explorer.exe as parent), not when run from a terminal.
powershell -NoProfile -Command "$self = Get-CimInstance Win32_Process -Filter ('ProcessId=' + $PID); $cmd = Get-CimInstance Win32_Process -Filter ('ProcessId=' + $self.ParentProcessId); $parent = Get-CimInstance Win32_Process -Filter ('ProcessId=' + $cmd.ParentProcessId); if ($cmd.CommandLine -match '\s/c\s' -and $parent.Name -eq 'explorer.exe') { exit 0 }; exit 1" >nul 2>&1
if not errorlevel 1 (
    echo.
    pause
)
exit /b %BUILD_EXIT%
