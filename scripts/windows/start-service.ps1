$ErrorActionPreference = "Stop"
$ScriptDir = $PSScriptRoot
. (Join-Path $ScriptDir "service-common.ps1")
Assert-Administrator
$WinswExe = Get-WinSwExecutable -ScriptDir $ScriptDir
Start-ManagedService -WinswExe $WinswExe
Write-Host "ProxySQL Admin UI is ready: http://localhost:8001"
