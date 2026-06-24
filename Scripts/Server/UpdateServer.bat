@echo off
setlocal

title RimWorld Together - Local Server Updater

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Update-Server.ps1" %*
set RC=%ERRORLEVEL%

if not "%RC%"=="0" (
    echo.
    echo Server updater failed with exit code %RC%.
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
