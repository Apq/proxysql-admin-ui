$ErrorActionPreference = "Stop"
$ScriptDir = $PSScriptRoot
. (Join-Path $ScriptDir "service-common.ps1")
Assert-Administrator
$WinswExe = Get-WinSwExecutable -ScriptDir $ScriptDir
Stop-ManagedService -WinswExe $WinswExe
Write-Host "ProxySQL Admin UI stopped."
