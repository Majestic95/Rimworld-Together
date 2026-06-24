@echo off
setlocal

title RimWorld Together MP Updater

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Update-RTMP.ps1" %*
set RC=%ERRORLEVEL%

if not "%RC%"=="0" (
    echo.
    echo Updater failed with exit code %RC%. See log at:
    echo   %LOCALAPPDATA%\RTMPUpdater\Update-RTMP.log
    echo.
    echo Press any key to close this window.
    pause >nul
    exit /b %RC%
)

echo.
echo Press any key to close this window.
timeout /t 5 >nul

endlocal
exit /b 0
