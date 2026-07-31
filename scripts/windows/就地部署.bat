@echo off
chcp 65001 >nul
setlocal

cd /d "%~dp0"
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy-inplace.ps1" %*
set "ExitCode=%ERRORLEVEL%"

echo.
if not "%ExitCode%"=="0" echo Deployment failed with exit code %ExitCode%.
echo Press any key to exit...
pause >nul
endlocal & exit /b %ExitCode%
