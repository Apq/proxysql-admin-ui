@echo off
chcp 65001 >nul
setlocal

cd /d "%~dp0"
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-service.ps1" -Uninstall
set "ExitCode=%ERRORLEVEL%"

echo.
echo Press any key to exit...
pause >nul
endlocal & exit /b %ExitCode%
