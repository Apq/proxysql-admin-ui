@echo off
chcp 65001 >nul
setlocal

cd /d "%~dp0"
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0restart-service.ps1"
set "ExitCode=%ERRORLEVEL%"

echo.
echo Press any key to exit...
pause >nul
endlocal & exit /b %ExitCode%
