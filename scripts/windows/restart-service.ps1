$ErrorActionPreference = "Stop"
$ScriptDir = $PSScriptRoot
. (Join-Path $ScriptDir "service-common.ps1")
Assert-Administrator
$WinswExe = Get-WinSwExecutable -ScriptDir $ScriptDir
Restart-ManagedService -WinswExe $WinswExe
Write-Host "ProxySQL Admin UI restarted: http://localhost:8000"
